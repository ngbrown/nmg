using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NMG.Core.Domain;
using Npgsql;
using System.Data;

namespace NMG.Core.Reader
{
    public class NpgsqlMetadataReader: IMetadataReader
    {
            private readonly string connectionStr;

            public NpgsqlMetadataReader(string connectionStr)
        {
            this.connectionStr = connectionStr;
        }


            #region IMetadataReader Members

            public IList<Column> GetTableDetails(Table table, string owner)
            {
                var columns = new List<Column>();
                var conn = new NpgsqlConnection(connectionStr);
                conn.Open();
                using (conn)
                {
                    using (NpgsqlCommand tableDetailsCommand = conn.CreateCommand())
                    {
                        

                        tableDetailsCommand.CommandText = string.Format(@"select
                            c.column_name
                            ,c.data_type
                            ,c.character_maximum_length
                            ,c.is_nullable
                            ,b.constraint_type as type
                            ,c.numeric_precision
                            ,c.numeric_scale
                            ,c.column_default
                            from information_schema.constraint_column_usage a
                            inner join information_schema.table_constraints b on a.constraint_name=b.constraint_name
                            inner join  information_schema.columns c on a.column_name=c.column_name and a.table_name=c.table_name
                            where a.table_schema='{1}' and a.table_name='{0}' and b.constraint_type in ('PRIMARY KEY')
                            union
                            select
                            a.column_name
                            ,a.data_type
                            ,a.character_maximum_length
                            ,a.is_nullable
                            ,b.constraint_type as type
                            ,a.numeric_precision
                            ,a.numeric_scale
                            ,a.column_default
                            from information_schema.columns a
                            inner join information_schema.table_constraints b on b.constraint_name ='{0}_'||a.column_name||'_fkey'
                            union
                            select 
                            a.column_name
                            ,a.data_type
                            ,a.character_maximum_length
                            ,a.is_nullable
                            ,''
                            ,a.numeric_precision
                            ,a.numeric_scale
                            ,a.column_default
                            from  information_schema.columns a
                            where a.table_schema='{1}' and a.table_name='{0}' and a.column_name not in (

                            select
                            c.column_name
                            from information_schema.constraint_column_usage a
                            inner join information_schema.table_constraints b on a.constraint_name=b.constraint_name
                            inner join  information_schema.columns c on a.column_name=c.column_name and a.table_name=c.table_name
                            where a.table_schema='{1}' and a.table_name='{0}' and b.constraint_type in ('PRIMARY KEY')
                            union
                            select
                            a.column_name
                            from information_schema.columns a
                            inner join information_schema.table_constraints b on b.constraint_name ='{0}_'||a.column_name||'_fkey')", table.Name, owner);


                        using (NpgsqlDataReader sqlDataReader = tableDetailsCommand.ExecuteReader(CommandBehavior.Default))
                        {
                            while (sqlDataReader.Read())
                            {
                                string columnName = sqlDataReader.GetString(0);
                                string dataType = sqlDataReader.GetString(1);
                                var characterMaxLenth = sqlDataReader["character_maximum_length"] as int?;
                                var numericPrecision = sqlDataReader["numeric_precision"] as int?;
                                var numericScale = sqlDataReader["numeric_scale"] as int?;
                                var columnDefault = sqlDataReader["column_default"] as string;
                                bool isNullable = sqlDataReader.GetString(3).Equals("YES",
                                                                                    StringComparison.
                                                                                        CurrentCultureIgnoreCase);
                                bool isPrimaryKey =
                                    (!sqlDataReader.IsDBNull(4)
                                         ? sqlDataReader.GetString(4).Equals(
                                             NpgsqlConstraintType.PrimaryKey.ToString(),
                                             StringComparison.CurrentCultureIgnoreCase)
                                         : false);
                                bool isIdentity =
                                    (columnDefault != null && columnDefault.ToLower().Contains("nextval") && isPrimaryKey) ? true : false;
                                bool isForeignKey =
                                    (!sqlDataReader.IsDBNull(4)
                                         ? sqlDataReader.GetString(4).Equals(
                                             NpgsqlConstraintType.ForeignKey.ToString(),
                                             StringComparison.CurrentCultureIgnoreCase)
                                         : false);

                                var m = new DataTypeMapper();

                                columns.Add(new Column
                                {
                                    Name = columnName,
                                    DataType = dataType,
                                    IsNullable = isNullable,
                                    IsPrimaryKey = isPrimaryKey,
                                    IsIdentity = isIdentity,
                                    IsForeignKey = isForeignKey,
                                    MappedDataType =
                                        m.MapFromDBType(ServerType.PostgreSQL, dataType, characterMaxLenth, numericPrecision, numericScale).ToString(),
                                    DataLength = characterMaxLenth,
                                    DataScale = numericScale,
                                    DataPrecision = numericPrecision
                                });

                                table.Columns = columns;
                            }
                            table.Owner = owner;
                            table.PrimaryKey = DeterminePrimaryKeys(table);

                            // Need to find reference informations associated with the FK
						    foreach (var c in table.Columns)
						    {
                                if (c.IsForeignKey)
                                {
                                    var reference = GetForeignKeyReference(table.Name, c.Name);

                                    if (reference != null)
                                    {
                                        c.ConstraintName = reference.ConstraintName;
                                        c.ForeignKeyTableName = reference.TableName;
                                        c.ForeignKeyColumnName = reference.ColumnName;
                                    }
                                }
						    }
                            table.ForeignKeys = DetermineForeignKeyReferences(table);
                            table.HasManyRelationships = DetermineHasManyRelationships(table);
                        }
                    }
                }
                return columns;
            }

            public IList<string> GetOwners()
            {
                var owners = new List<string>();
                var conn = new NpgsqlConnection(connectionStr);
                conn.Open();
                using (conn)
                {
                    var tableCommand = conn.CreateCommand();
                    tableCommand.CommandText = @"select distinct table_schema from information_schema.tables
                                                union
                                                select schema_name from information_schema.schemata
                                                ";
                    var sqlDataReader = tableCommand.ExecuteReader(CommandBehavior.CloseConnection);
                    while (sqlDataReader.Read())
                    {
                        var ownerName = sqlDataReader.GetString(0);
                        owners.Add(ownerName);
                    }
                }

                return owners;
            }

            public List<Table> GetTables(string owner)
            {
                var tables = new List<Table>();
                var conn = new NpgsqlConnection(connectionStr);
                conn.Open();
                using (conn)
                {
                    var tableCommand = conn.CreateCommand();
                    tableCommand.CommandText = String.Format("select table_name from information_schema.tables where table_type like 'BASE TABLE' and TABLE_SCHEMA = '{0}'", owner);
                    var sqlDataReader = tableCommand.ExecuteReader(CommandBehavior.CloseConnection);
                    while (sqlDataReader.Read())
                    {
                        var tableName = sqlDataReader.GetString(0);
                        tables.Add(new Table { Name = tableName });
                    }
                }
                tables.Sort((x, y) => x.Name.CompareTo(y.Name));
                return tables;
            }
            public List<string> GetSequences(string owner)
            {
                return null;
            }
            public string GetSequences(string tablename,string owner,string column)
            {                
                var conn = new NpgsqlConnection(connectionStr);
                conn.Open();
                string tableName = "";
                using (conn)
                {
                    NpgsqlCommand seqCommand = conn.CreateCommand();
                    seqCommand.CommandText = @"select 
b.sequence_name
from
information_schema.columns a
inner join information_schema.sequences b on a.column_default like 'nextval(\''||b.sequence_name||'%'
where
a.table_schema='" + owner+"' and a.table_name='"+tablename+"' and a.column_name='"+column+"'";
                    NpgsqlDataReader seqReader = seqCommand.ExecuteReader(CommandBehavior.CloseConnection);
                   
                    while (seqReader.Read())
                    {
                         tableName = seqReader.GetString(0);

                       // sequences.Add(tableName);
                    }
                }
                return tableName;
            }
            public List<string> GetSequences(List<Table> tables)
            {
                var sequences = new List<string>();
                var conn = new NpgsqlConnection(connectionStr);
                conn.Open();
                using (conn)
                {
                    NpgsqlCommand seqCommand = conn.CreateCommand();
                    seqCommand.CommandText = "select sequence_name from information_schema.sequences";
                    NpgsqlDataReader seqReader = seqCommand.ExecuteReader(CommandBehavior.CloseConnection);
                    while (seqReader.Read())
                    {
                        string tableName = seqReader.GetString(0);
                        
                        sequences.Add(tableName);
                    }
                }
                return sequences;
            }

            #endregion

        public PrimaryKey DeterminePrimaryKeys(Table table)
        {
            var primaryKeys = table.Columns.Where(x => x.IsPrimaryKey.Equals(true)).ToList();

            if (primaryKeys.Count() == 1)
            {
                Column c = primaryKeys.First();
                var key = new PrimaryKey
                {
                    Type = PrimaryKeyType.PrimaryKey,
                    Columns = { c }
                };
                return key;
            }

            if (primaryKeys.Count() > 1)
            {
                var key = new PrimaryKey
                {
                    Type = PrimaryKeyType.CompositeKey,
                    Columns = primaryKeys
                };
 
                return key;
            }

            return null;
        }

        public IList<ForeignKey> DetermineForeignKeyReferences(Table table)
        {
            var foreignKeys = (from c in table.Columns
                               where c.IsForeignKey
                               group c by new { c.ConstraintName, c.ForeignKeyTableName, c.IsNullable } into g
                               select new ForeignKey
                               {
                                   Name = g.Key.ConstraintName,
                                   IsNullable = g.Key.IsNullable,
                                   References = g.Key.ForeignKeyTableName,
                                   Columns = g.ToList(),
                                   UniquePropertyName = g.Key.ForeignKeyTableName
                               }).ToList();

            Table.SetUniqueNamesForForeignKeyProperties(foreignKeys);

            return foreignKeys;
        }

        /// <summary>
        /// Search for one or more columns that make up the foreign key.
        /// </summary>
        /// <param name="columns">All columns that could be used for the foreign key</param>
        /// <param name="foreignKeyName">Name of the foreign key constraint</param>
        /// <returns>List of columns associated with the foreign key</returns>
        /// <remarks>Composite foreign key will return multiple columns</remarks>
        private IList<Column> DetermineColumnsForForeignKey(IList<Column> columns, string foreignKeyName)
        {
            return (from c in columns
                    where c.IsForeignKey && c.ConstraintName == foreignKeyName
                    select c).ToList();
        }

        private ForeignKeyReference GetForeignKeyReference(string selectedTableName, string columnName)
        {
            var conn = new Npgsql.NpgsqlConnection(connectionStr);
            conn.Open();
            using (conn)
            {
                NpgsqlCommand tableCommand = conn.CreateCommand();
                tableCommand.CommandText = String.Format(
                    @"
                        select c.constraint_name, pk.table_name, pt.column_name
                        from information_schema.referential_constraints c
                        inner join information_schema.table_constraints fk on c.constraint_name = fk.constraint_name
                        inner join information_schema.table_constraints pk on c.unique_constraint_name = pk.constraint_name
                        inner join information_schema.key_column_usage cu on c.constraint_name = cu.constraint_name
                        inner join (
                        select i1.table_name, i2.column_name
                        from information_schema.table_constraints i1
                        inner join information_schema.key_column_usage i2 on i1.constraint_name = i2.constraint_name
                        where i1.constraint_type = 'PRIMARY KEY'
                        ) pt on pt.table_name = pk.table_name
                        where fk.table_name = '{0}' and cu.column_name = '{1}'",
                    selectedTableName, columnName);
                var sqlDataReader = tableCommand.ExecuteReader();

                if (sqlDataReader.HasRows)
                {
                    sqlDataReader.Read();
                    
                    return new ForeignKeyReference()
                        {
                            ConstraintName = sqlDataReader["constraint_name"] as string,
                            TableName = sqlDataReader["table_name"] as string,
                            ColumnName = sqlDataReader["column_name"] as string,
                        };
                }

                return null;
            }
        }



        // http://blog.sqlauthority.com/2006/11/01/sql-server-query-to-display-foreign-key-relationships-and-name-of-the-constraint-for-each-table-in-database/
        private IList<HasMany> DetermineHasManyRelationships(Table table)
        {
            var hasManyRelationships = new List<HasMany>();
            var conn = new NpgsqlConnection(connectionStr);
            conn.Open();
            using (conn)
            {
                using (var command = new NpgsqlCommand())
                {
                    command.Connection = conn;
                    command.CommandText =
                        String.Format(
                            @"
                        select DISTINCT
	                         b.TABLE_NAME,
	                         c.TABLE_NAME
                        from
	                        INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS a
	                        join
	                        INFORMATION_SCHEMA.TABLE_CONSTRAINTS b
	                        on
	                        a.CONSTRAINT_SCHEMA = b.CONSTRAINT_SCHEMA and
	                        a.UNIQUE_CONSTRAINT_NAME = b.CONSTRAINT_NAME
	                        join
	                        INFORMATION_SCHEMA.TABLE_CONSTRAINTS c
	                        on
	                        a.CONSTRAINT_SCHEMA = c.CONSTRAINT_SCHEMA and
	                        a.CONSTRAINT_NAME = c.CONSTRAINT_NAME
                        where
	                        b.TABLE_NAME = '{0}'
                        order by
	                        1,2",
                            table.Name);
                    NpgsqlDataReader reader = command.ExecuteReader();

                    while (reader.Read())
                    {
                        hasManyRelationships.Add(new HasMany
                        {
                            Reference = reader.GetString(1)
                        });
                    }

                    return hasManyRelationships;
                }
            }
        }

        private class ForeignKeyReference
        {
            public string ConstraintName { get; set; }
            public string TableName { get; set; }
            public string ColumnName { get; set; }
        }
    }
}
