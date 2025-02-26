﻿using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ZeroQL.SourceGenerators.Resolver.Context;

namespace ZeroQL.SourceGenerators.Resolver;

public class GraphQLSourceResolver
{
    public static string Resolve(
        SemanticModel semanticModel,
        GraphQLSourceGenerationContext context)
    {
        var inputTypeName = context.GraphQLMethodInputSymbol.ToSafeGlobalName();
        var typeInfo = context.UploadProperties.ToDictionary(o => o.SafeName);
        var source = $@"// This file generated for ZeroQL.
// <auto-generated/>
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using ZeroQL;
using ZeroQL.Stores;
using ZeroQL.Json;
using ZeroQL.Internal;

#nullable enable

namespace {semanticModel.Compilation.Assembly.Name}
{{
    {SourceGeneratorInfo.CodeGenerationAttribute}
    internal static class ZeroQLModuleInitializer_{context.KeyHash}
    {{
        [global::System.Runtime.CompilerServices.ModuleInitializer]
        public static void Init()
        {{
            GraphQLQueryStore<{context.QueryTypeName}>.Executor[{SyntaxFactory.Literal(context.Key).Text}] = Execute;
            GraphQLQueryStore<{context.QueryTypeName}>.Query[{SyntaxFactory.Literal(context.Key).Text}] = new QueryInfo 
            {{
                Query = {SyntaxFactory.Literal(context.OperationQuery).Text},
                QueryBody = {SyntaxFactory.Literal(context.OperationQueryBody).Text},
                OperationType = {SyntaxFactory.Literal(context.OperationType).Text},
                Hash = {SyntaxFactory.Literal(context.OperationHash).Text},
            }};
        }}

        public static async Task<GraphQLResult<{context.QueryTypeName}>> Execute(QueryExecuteContext context)
        {{
            var qlClient = context.Client; 
            var variables = ({context.RequestExecutorInputSymbol.ToGlobalName()})context.Variables!;
            var qlResponse = await qlClient.QueryPipeline.ExecuteAsync<{context.QueryTypeName}>(qlClient.HttpClient, context.QueryKey, context.Variables, context.CancellationToken, queryRequest => 
            {{
                {GraphQLUploadResolver.GenerateRequestPreparations(inputTypeName, context.ExecutionStrategy, typeInfo)}
                return content;
            }});

            if (qlResponse is null)
            {{
                return new GraphQLResult<{context.QueryTypeName}>
                {{
                    Errors = new[]
                    {{
                        new GraphQueryError {{ Message = ""Failed to deserialize response"" }}
                    }}
                }};
            }}

            if (qlResponse.Errors?.Length > 0)
            {{
                return new GraphQLResult<{context.QueryTypeName}>
                {{
                    Query = qlResponse.Query,
                    Errors = qlResponse.Errors,
                    Extensions = qlResponse.Extensions
                }};
            }}

            return new GraphQLResult<{context.QueryTypeName}>
            {{
                Query = qlResponse.Query,
                Data = qlResponse.Data,
                Extensions = qlResponse.Extensions
            }};
        }}

        {GraphQLUploadResolver.GenerateUploadsSelectors(context.ExecutionStrategy, context.UploadProperties, context.UploadType)}
    }}
}}";
        return source;
    }
}