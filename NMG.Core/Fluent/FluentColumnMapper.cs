using System.Text;
using NMG.Core.Domain;
using NMG.Core.TextFormatter;

namespace NMG.Core.Fluent
{
    public class FluentColumnMapper
    {
        public string Map(Column column, string fieldName, ITextFormatter Formatter, bool includeLengthAndScale = true)
        {
            var mappedStrBuilder = new StringBuilder(string.Format("Map(x => x.{0})", fieldName));
            mappedStrBuilder.Append(Constants.Dot);
            mappedStrBuilder.Append("Column(\"" + column.Name + "\")");

            if (!column.IsNullable)
            {
                mappedStrBuilder.Append(Constants.Dot);
                mappedStrBuilder.Append("Not.Nullable()");
            }

            if (column.IsUnique)
            {
                mappedStrBuilder.Append(Constants.Dot);
                mappedStrBuilder.Append("Unique()");
            }

            if (column.DataLength.HasValue && column.DataLength.Value > 0 && includeLengthAndScale)
            {
                mappedStrBuilder.Append(Constants.Dot);
                mappedStrBuilder.Append("Length(" + column.DataLength + ")");
            }
            else
            {
                if (column.DataPrecision > 0 && includeLengthAndScale)
                {
                    mappedStrBuilder.Append(Constants.Dot);
                    mappedStrBuilder.Append("Precision(" + column.DataPrecision.Value + ")");
                }

                if (column.DataScale > 0 && includeLengthAndScale)
                {
                    mappedStrBuilder.Append(Constants.Dot);
                    mappedStrBuilder.Append("Scale(" + column.DataScale.Value + ")");
                }
            }


            mappedStrBuilder.Append(Constants.SemiColon);
            return mappedStrBuilder.ToString();
        }
    }
}