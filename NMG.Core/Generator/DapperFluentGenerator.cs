using System;
using System.CodeDom;
using System.Linq;
using System.Text;
using NMG.Core.Domain;
using NMG.Core.Fluent;
using NMG.Core.TextFormatter;

namespace NMG.Core.Generator
{
    public class DapperFluentGenerator : AbstractCodeGenerator
    {
        private readonly ApplicationPreferences appPrefs;

        public DapperFluentGenerator(ApplicationPreferences appPrefs, Table table)
            : base(appPrefs.FolderPath, "Mapping", appPrefs.TableName, appPrefs.NameSpaceMap, appPrefs.AssemblyName, appPrefs.Sequence, table, appPrefs)
        {
            this.appPrefs = appPrefs;
            language = this.appPrefs.Language;
        }

        public override void Generate(bool writeToFile = true)
        {
            var pascalCaseTextFormatter = new PascalCaseTextFormatter {PrefixRemovalList = appPrefs.FieldPrefixRemovalList};
            var className = string.Format("{0}{1}{2}", appPrefs.ClassNamePrefix, pascalCaseTextFormatter.FormatSingular(Table.Name), "Map");
            var compileUnit = GetCompleteCompileUnit(className);
            var generateCode = GenerateCode(compileUnit, className);

            if (writeToFile)
            {
                WriteToFile(generateCode, className);
            }
            else
            {
                GeneratedCode = WriteToString(compileUnit, GetCodeDomProvider());
            }
        }

        protected override string CleanupGeneratedFile(string generatedContent)
        {
            return generatedContent;
        }

        public CodeCompileUnit GetCompleteCompileUnit(string className)
        {
            var codeGenerationHelper = new CodeGenerationHelper();
            var compileUnit = codeGenerationHelper.GetCodeCompileUnit(nameSpace, className);

            var newType = compileUnit.Namespaces[0].Types[0];

            newType.IsPartial = appPrefs.GeneratePartialClasses;
            var pascalCaseTextFormatter = new PascalCaseTextFormatter {PrefixRemovalList = appPrefs.FieldPrefixRemovalList};
            newType.BaseTypes.Add(string.Format("DommelEntityMap<{0}{1}>", appPrefs.ClassNamePrefix, pascalCaseTextFormatter.FormatSingular(Table.Name)));

            var constructor = new CodeConstructor {Attributes = MemberAttributes.Public};
            constructor.Statements.Add(new CodeSnippetStatement(TABS3 + "ToTable(\"" + Table.Name + "\");"));

            if (UsesSequence)
            {
                var fieldName = FixPropertyWithSameClassName(Table.PrimaryKey.Columns[0].Name, Table.Name);
                constructor.Statements.Add(new CodeSnippetStatement(String.Format(TABS3 + "Map(x => x.{0}).ToColumn(x => x.{1}).IsKey()", Formatter.FormatText(fieldName), fieldName, appPrefs.Sequence)));
            }
            else if (Table.PrimaryKey != null && Table.PrimaryKey.Type == PrimaryKeyType.PrimaryKey)
            {
                var fieldName = FixPropertyWithSameClassName(Table.PrimaryKey.Columns[0].Name, Table.Name);
                constructor.Statements.Add(GetIdMapCodeSnippetStatement(appPrefs, Table, Table.PrimaryKey.Columns[0].Name, fieldName, Table.PrimaryKey.Columns[0].DataType, Formatter));
            }
            else if (Table.PrimaryKey != null)
            {
                foreach (var pkColumn in Table.PrimaryKey.Columns)
                {
                    var propertyName = Formatter.FormatText(pkColumn.Name);
                    var fieldName = FixPropertyWithSameClassName(propertyName, Table.Name);
                    var columnMapping = new DapperFluentColumnMapper().Map(pkColumn, fieldName, Formatter, appPrefs.IncludeLengthAndScale);
                    constructor.Statements.Add(new CodeSnippetStatement(TABS3 + columnMapping));
                }
            }

            // Many To One Mapping
            foreach (var fk in Table.ForeignKeys.Where(fk => fk.Columns.First().IsForeignKey && appPrefs.IncludeForeignKeys))
            {
                var propertyName = appPrefs.NameFkAsForeignTable ? fk.UniquePropertyName : fk.Columns.First().Name;
                propertyName = Formatter.FormatSingular(propertyName);
                var fieldName = FixPropertyWithSameClassName(propertyName, Table.Name);
                
                var propertyMapType = "HasRequired";
                if (fk.IsNullable)
                {
                    propertyMapType = "HasOptional";
                }

                var codeSnippet = string.Format(TABS3 + "{0}(x => x.{1}).WithMany(t => t.{2}).HasForeignKey(d => d.{3});", propertyMapType, fieldName, fk.Columns.First().ForeignKeyTableName, fk.Columns.First().ForeignKeyColumnName);
                constructor.Statements.Add(new CodeSnippetStatement(codeSnippet));
            }

            foreach (var column in Table.Columns.Where(x => !x.IsPrimaryKey && (!x.IsForeignKey || !appPrefs.IncludeForeignKeys)))
            {
                var propertyName = Formatter.FormatText(column.Name);
                var fieldName = FixPropertyWithSameClassName(propertyName, Table.Name);
                var columnMapping = new DapperFluentColumnMapper().Map(column, fieldName, Formatter, appPrefs.IncludeLengthAndScale);
                constructor.Statements.Add(new CodeSnippetStatement(TABS3 + columnMapping));
            }

            if (appPrefs.IncludeHasMany)
            {
                Table.HasManyRelationships.ToList().ForEach(x => constructor.Statements.Add(new EFOneToMany(Formatter, pascalCaseTextFormatter).Create(x)));
            }

            newType.Members.Add(constructor);
            return compileUnit;
        }

        private static string FixPropertyWithSameClassName(string property, string className)
        {
            return property.ToLowerInvariant() == className.ToLowerInvariant() ? property + "Val" : property;
        }

        protected override string AddStandardHeader(string entireContent)
        {
            entireContent = "using " + appPrefs.NameSpace + "; " + entireContent;
            entireContent = string.Format("using System.ComponentModel.DataAnnotations.Schema;{{0}}using System.Data.Entity.ModelConfiguration;{{0}}{0}", Environment.NewLine) + entireContent;
            return base.AddStandardHeader(entireContent);
        }

        private static CodeSnippetStatement GetIdMapCodeSnippetStatement(ApplicationPreferences appPrefs, Table table, string pkColumnName, string propertyName, string pkColumnType, ITextFormatter formatter)
        {
            var fieldName = FixPropertyWithSameClassName(propertyName, table.Name);
            var pkAlsoFkQty = (from fk in table.ForeignKeys.Where(fk => fk.UniquePropertyName == pkColumnName) select fk).Count();
            if (pkAlsoFkQty > 0) fieldName = fieldName + "Id";
            return new CodeSnippetStatement(string.Format(TABS3 + "Map(x => x.{0}).ToColumn(\"{1}\").IsKey();", formatter.FormatText(fieldName),  pkColumnName));
        }
    }
}