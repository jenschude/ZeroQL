﻿using NodaTime;

namespace ZeroQL.TestServer.Query;

[ExtendObjectType(typeof(Query))]
public class NodeTimeGraphQLExtensions
{
    public Instant GetInstant()
    {
        return Instant.FromDateTimeOffset(new DateTimeOffset(2042, 6, 6, 6, 6, 6, TimeSpan.Zero));
    }
    
    public ZonedDateTime GetZonedDateTime()
    {
        return Instant.FromDateTimeOffset(new DateTimeOffset(2042, 6, 6, 6, 6, 6, TimeSpan.Zero)).InUtc();
    }
}

[ExtendObjectType(typeof(Mutation))]
public class NodeTimeGraphQLMutations
{
    public Instant CreateInstant(Instant instant)
    {
        return instant;
    }
}