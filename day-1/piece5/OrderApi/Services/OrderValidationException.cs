namespace OrderApi.Services;

public sealed class OrderValidationException(string message) : Exception(message);
