using System.Globalization;
using Prognode.Contracts.Tags;
using Prognode.Web;

/// <summary>Historian CSV values: the tag's decimal places, no float noise, the region's decimal separator.</summary>
internal static class CsvNumberContract
{
    public static void Run()
    {
        var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? "," : ".";
        string N(string text) => text.Replace(".", sep);
        TagDefinition Tag(TagDataType type, int places) => new(Guid.NewGuid(), Guid.NewGuid(), "tag1", "D100", type, null, default, "", 1, 0, places, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // A REAL read from the PLC is stored as a double: 26.67311f becomes 26.673110961914062.
        double real = 26.67311f;
        Require(EndpointExtensions.CsvNumber(real, Tag(TagDataType.Float32, 2)) == N("26.67"), "REAL with 2 decimal places");
        Require(EndpointExtensions.CsvNumber(real, Tag(TagDataType.Float32, 0)) == N("26.67311"), "REAL without decimal places keeps 7 digits, no noise");
        Require(EndpointExtensions.CsvNumber(58942474f, Tag(TagDataType.Float32, 0)) == "58942470", "large REAL has no exponent");
        Require(EndpointExtensions.CsvNumber(-0.5, Tag(TagDataType.Float64, 0)) == N("-0.5"), "negative, ASCII minus");
        Require(EndpointExtensions.CsvNumber(12.3, Tag(TagDataType.Int16, 1)) == N("12.3"), "implied decimals");
        Require(EndpointExtensions.CsvNumber(1234567, Tag(TagDataType.Int32, 0)) == "1234567", "integers unchanged");
        Require(EndpointExtensions.CsvNumber(null, Tag(TagDataType.Float32, 2)) == "", "no value");
        Console.WriteLine("PASS: historian CSV numbers (decimal places, REAL precision, region separator)");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
