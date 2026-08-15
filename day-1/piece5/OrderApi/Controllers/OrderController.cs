using Microsoft.AspNetCore.Mvc;
using OrderApi.DTOs;
using OrderApi.Services;

namespace OrderApi.Controllers;

[ApiController]
[Route("api/orders")]
public class OrderController(
    IOrderService orderService,
    ILogger<OrderController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(
        typeof(OrderResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await orderService.CreateOrderAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetOrder),
                new { id = order.Id },
                order);
        }
        catch (OrderValidationException ex)
        {
            logger.LogWarning(
                ex,
                "Order validation failed for {CustomerEmail}",
                request.CustomerEmail);

            return BadRequest(new
            {
                title = "Order validation failed",
                detail = ex.Message
            });
        }
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(
        typeof(OrderResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetOrder(
        int id,
        CancellationToken cancellationToken)
    {
        var order = await orderService.GetOrderAsync(
            id,
            cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        return Ok(order);
    }
}