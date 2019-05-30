using System.Text;
using NMG.Core.Domain;
using NMG.Core.TextFormatter;

namespace NMG.Core.Fluent
{
    public class DapperFluentColumnMapper
    {
        public string Map(Column column, string fieldName, ITextFormatter Formatter, bool includeLengthAndScale = true)
        {
            var mappedStrBuilder = new StringBuilder(string.Format("Map(x => x.{0})", fieldName));
            mappedStrBuilder.Append(Constants.Dot);
            mappedStrBuilder.Append("ToColumn(\"" + column.Name + "\")");

            mappedStrBuilder.Append(Constants.SemiColon);
            return mappedStrBuilder.ToString();
        }
    }
}