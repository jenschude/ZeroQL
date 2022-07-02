﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqQL.SourceGenerators
{
    [Generator]
    public class GraphQLQuerySourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new QueryMethodSelector());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is QueryMethodSelector receiver))
            {
                return;
            }

            var queries = new Dictionary<string, string>();
            foreach (var invocation in receiver.Invocations)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
                {
                    break;
                }

                var semanticModel = context.Compilation.GetSemanticModel(invocation.SyntaxTree);
                var possibleMethod = ModelExtensions.GetSymbolInfo(semanticModel, memberAccess.Name);
                if (!(possibleMethod.Symbol is IMethodSymbol method) ||
                    !(method.ContainingSymbol is INamedTypeSymbol containingType) ||
                    containingType.ConstructedFrom.ToString() != "LinqQL.Core.GraphQLClient<TQuery>")
                {
                    break;
                }

                if (context.CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var key = invocation.ArgumentList.Arguments.Last().ToString();
                var query = GetQuery(semanticModel, method, invocation);

                if (context.CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                queries[key] = query;
            }

            var source = @$"// This file generated for LinqQL.
// <auto-generated/>
using System;
using LinqQL.Core;

namespace {context.Compilation.Assembly.Name}
{{
    {SourceGeneratorInfo.CodeGenerationAttribute}
    public static class LinqQLModuleInitializer
    {{
        [global::System.Runtime.CompilerServices.ModuleInitializer]
        public static void Init()
        {{
{queries.Select(o => $@"            GraphQLQueryStore.Query.Add({SyntaxFactory.Literal(o.Key).Text}, {SyntaxFactory.Literal(o.Value).Text});").JoinWithNewLine()}
        }}
    }}
}}";

            if (context.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            context.AddSource("LinqQLModuleInitializer.g.cs", source);

            string GetQuery(SemanticModel semanticModel, IMethodSymbol method, InvocationExpressionSyntax invocation)
            {
                var parameterNames = method.Parameters
                    .Select(p => p.Name)
                    .ToArray();

                var queryExpression = invocation.ArgumentList.Arguments.Last().Expression;
                if (parameterNames.SequenceEqual(new[] { "name", "query", "queryKey" }))
                {
                    return GenerateGraphQLQuery(semanticModel, invocation.ArgumentList.Arguments.First().ToString(), null, queryExpression);
                }

                if (parameterNames.SequenceEqual(new[] { "variables", "query", "queryKey" }))
                {
                    var variablesExpression = invocation.ArgumentList.Arguments.First().Expression;
                    return GenerateGraphQLQuery(semanticModel, string.Empty, variablesExpression, queryExpression);
                }

                if (parameterNames.SequenceEqual(new[] { "query", "queryKey" }))
                {
                    return GenerateGraphQLQuery(semanticModel, string.Empty, null, queryExpression);
                }

                return Failed(invocation);
            }

            string GenerateGraphQLQuery(SemanticModel semanticModel, string name, ExpressionSyntax? variablesExpression, ExpressionSyntax query)
            {
                if (!(query is LambdaExpressionSyntax lambda))
                {
                    return "";
                }

                var inputs = GetQueryInputs(lambda);
                var variables = GetVariables(semanticModel, variablesExpression);
                var availableVariables = inputs.VariablesName is null ? new Dictionary<string, string>()
                    : variables
                        .ToDictionary(
                            o => $"{inputs.VariablesName}.{o.Name}",
                            o => "$" + o.Name.FirstToLower());

                var generationContext = new GraphQLQueryGenerationContext(inputs.QueryName, availableVariables, semanticModel);
                var body = GenerateBody(generationContext, lambda.Body);

                if (context.CancellationToken.IsCancellationRequested)
                {
                    return string.Empty;
                }

                var stringBuilder = new StringBuilder();
                stringBuilder.Append("query");
                if (!string.IsNullOrEmpty(name))
                {
                    stringBuilder.Append($" {name}");
                }
                if (inputs.VariablesName != null)
                {
                    var variablesBody = variables
                        .Select(o => $"${o.Name.FirstToLower()}: {o.Type}")
                        .Join()
                        .Wrap(" (", ")");

                    stringBuilder.Append(variablesBody);
                }
                stringBuilder.Append(" { ");
                stringBuilder.Append(body);
                stringBuilder.Append("}");

                return stringBuilder.ToString();
            }

            static (string? VariablesName, string QueryName) GetQueryInputs(LambdaExpressionSyntax lambda)
            {
                if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
                {
                    return (null, simpleLambda.Parameter.Identifier.ValueText);
                }

                if (lambda is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
                {
                    var parameters = parenthesizedLambda.ParameterList.Parameters;
                    return (parameters.First().Identifier.ValueText, parameters.Last().Identifier.ValueText);
                }

                return default;
            }

            (string Name, string Type)[] GetVariables(SemanticModel semanticModel, ExpressionSyntax? variablesExpression)
            {
                if (variablesExpression is null)
                {
                    return Array.Empty<(string Name, string Type)>();
                }

                if (!(variablesExpression is AnonymousObjectCreationExpressionSyntax anonymousObject))
                {
                    Failed(variablesExpression);
                    return Array.Empty<(string Name, string Type)>();
                }

                var ctor = semanticModel.GetSymbolInfo(anonymousObject).Symbol as IMethodSymbol;
                var type = ctor!.ContainingType;
                return type.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Select(o => (o.Name, o.Type.ToStringWithNullable()))
                    .ToArray();
            }

            string GenerateBody(GraphQLQueryGenerationContext generationContext, CSharpSyntaxNode node)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    return string.Empty;
                }

                switch (node)
                {
                    case InvocationExpressionSyntax invocation:
                    {
                        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                            memberAccess.Expression is IdentifierNameSyntax identifierName &&
                            identifierName.Identifier.ValueText != generationContext.QueryVariableName)
                        {
                            return Failed(identifierName, Descriptors.DontUserOutScopeValues);
                        }

                        var symbol = generationContext.SemanticModel.GetSymbolInfo(invocation);
                        if (!(symbol.Symbol is IMethodSymbol method))
                        {
                            return Failed(invocation);
                        }

                        var argumentNames = method.Parameters
                            .Take(method.Parameters.Length - 1)
                            .Select(o => $"{o.Name.FirstToLower()}: ")
                            .ToArray();

                        var stringBuilder = new StringBuilder();
                        stringBuilder.Append(method.Name.FirstToLower());
                        if (argumentNames.Any())
                        {
                            var graphQLArguments = invocation.ArgumentList.Arguments
                                .Take(argumentNames.Length)
                                .Select((o, i) => $"{argumentNames[i]}{GenerateBody(generationContext, o)}")
                                .Join()
                                .Wrap("(", ")");

                            stringBuilder.Append(graphQLArguments);
                        }
                        stringBuilder.Append($" {{ {GenerateBody(generationContext, invocation.ArgumentList.Arguments.Last().Expression)} }} ");

                        return stringBuilder.ToString();
                    }
                    case MemberAccessExpressionSyntax member:
                    {
                        if (member.Expression is MemberAccessExpressionSyntax left)
                        {
                            return GenerateBody(generationContext, left);
                        }

                        if (member.Expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == generationContext.QueryVariableName)
                        {
                            return member.Name.Identifier.ValueText.FirstToLower();
                        }

                        return Failed(member.Expression);
                    }
                    case IdentifierNameSyntax identifierNameSyntax:
                    {
                        if (identifierNameSyntax.Identifier.ValueText == generationContext.QueryVariableName)
                        {
                            return string.Empty;
                        }

                        return Failed(node);
                    }
                    case SimpleLambdaExpressionSyntax simpleLambda:
                    {
                        var parameter = simpleLambda.Parameter.Identifier.ValueText;
                        var childGenerationContext = new GraphQLQueryGenerationContext(
                            parameter,
                            generationContext.AvailableVariables,
                            generationContext.SemanticModel);

                        return GenerateBody(childGenerationContext, simpleLambda.Body);
                    }
                    case ArgumentSyntax argument:
                    {
                        if (argument.Expression is LiteralExpressionSyntax literal)
                        {
                            return literal.ToString();
                        }

                        var value = argument.Expression.ToString();
                        if (generationContext.AvailableVariables.ContainsKey(value))
                        {
                            return generationContext.AvailableVariables[value];
                        }

                        if (argument.Expression is MemberAccessExpressionSyntax memberAccess)
                        {
                            var symbol = generationContext.SemanticModel.GetSymbolInfo(memberAccess.Expression);
                            if (!(symbol.Symbol is INamedTypeSymbol namedType))
                            {
                                return Failed(memberAccess);
                            }

                            if (namedType.EnumUnderlyingType != null)
                            {
                                return memberAccess.Name.ToString();
                            }
                        }

                        return Failed(argument);
                    }
                    case AnonymousObjectCreationExpressionSyntax anonymous:
                    {
                        return anonymous.Initializers
                            .Select(o => GenerateBody(generationContext, o))
                            .Join(" ");
                    }
                    case AnonymousObjectMemberDeclaratorSyntax anonymousMember:
                    {
                        return GenerateBody(generationContext, anonymousMember.Expression);
                    }
                }

                return Failed(node);
            }

            string Failed(CSharpSyntaxNode node, DiagnosticDescriptor? descriptor = null)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    return string.Empty;
                }
                
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor ?? Descriptors.FailedToConvert,
                        node.GetLocation(),
                        node.ToString()));

                return $"// Failed to generate query for: {node.ToString()}";
            }
        }
    }
}