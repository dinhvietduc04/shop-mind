using Shop.Domain.Entities;
using Shop.Domain.Enums;
using Shop.Domain.Exceptions;
using Shop.Domain.Rules;

namespace Shop.UnitTests;

public class OrderTransitionsTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Paid, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.PaymentFailed, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.Processing, true)]
    [InlineData(OrderStatus.Paid, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Processing, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Paid, OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Paid, false)]
    public void IsValidTransition_ReturnsExpected(OrderStatus from, OrderStatus to, bool expected)
    {
        Assert.Equal(expected, OrderTransitions.IsValidTransition(from, to));
    }

    [Fact]
    public void Cancel_FromPending_Succeeds()
    {
        var order = new Order { Status = OrderStatus.Pending };
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_FromPaid_Succeeds()
    {
        var order = new Order { Status = OrderStatus.Paid };
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void Cancel_FromNonCancellable_Throws(OrderStatus status)
    {
        var order = new Order { Status = status };
        Assert.Throws<InvalidOrderStateException>(() => order.Cancel());
    }

    [Fact]
    public void TransitionTo_Invalid_Throws()
    {
        var order = new Order { Status = OrderStatus.Pending };
        Assert.Throws<InvalidOrderStateException>(() => order.TransitionTo(OrderStatus.Delivered));
    }
}
