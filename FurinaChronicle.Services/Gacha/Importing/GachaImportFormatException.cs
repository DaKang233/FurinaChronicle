namespace FurinaChronicle.Services.Gacha.Importing;

public sealed class GachaImportFormatException : Exception
{
    public GachaImportFormatException(string message)
        : base(message)
    {
    }

    public GachaImportFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
