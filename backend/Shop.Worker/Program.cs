using Microsoft.EntityFrameworkCore;
using Shop.Application.Interfaces.Events;
using Shop.Infrastructure.Messaging.Consumers;
using Shop.Infrastructure.Persistence;
using Shop.Worker;
using Shop.Worker.Consumers;
using Shop.Worker.RabbitMq;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(
    builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=shopmind;Username=postgres;Password=postgres"));

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.Configure<WorkerOutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection("OutboxRetention"));
builder.Services.Configure<DataRetentionOptions>(builder.Configuration.GetSection("Retention"));

builder.Services.AddSingleton<IBrokerPublisher, RabbitMqPublisher>();
builder.Services.AddTransient<AnalyticsEventHandler>();

builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddHostedService<EmailConsumerWorker>();
builder.Services.AddHostedService<AnalyticsConsumerWorker>();
builder.Services.AddHostedService<PaymentWebhookQueueWorker>();
builder.Services.AddHostedService<OutboxRetentionWorker>();
builder.Services.AddHostedService<RetentionSweeperWorker>();

var host = builder.Build();
host.Run();
