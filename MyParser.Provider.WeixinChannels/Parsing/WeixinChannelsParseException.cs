namespace MyParser.Provider.WeixinChannels.Parsing;

public sealed class WeixinChannelsParseException(string message, Exception? innerException = null) : Exception(message, innerException);
