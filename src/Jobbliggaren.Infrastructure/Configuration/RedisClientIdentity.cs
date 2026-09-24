namespace Jobbliggaren.Infrastructure.Configuration;

internal enum RedisClientIdentity
{
    ApiPersistent,
    WorkerPersistent,
    ApiVolatile,
}
