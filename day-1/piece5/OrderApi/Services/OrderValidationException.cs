namespace OrderApi.Services;

public class OrderValidationException(string message)
    : Exception(message);