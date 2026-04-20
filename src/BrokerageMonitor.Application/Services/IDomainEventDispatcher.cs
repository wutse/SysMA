using BrokerageMonitor.Domain.Events;

namespace BrokerageMonitor.Application.Services;

/// <summary>
/// Abstracts domain-event publication so infrastructure services remain decoupled from
/// the concrete event bus (MediatR, in-memory, etc.).
/// Implemented by the Application layer composition root (EP-005).
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IDomainEvent;
}
