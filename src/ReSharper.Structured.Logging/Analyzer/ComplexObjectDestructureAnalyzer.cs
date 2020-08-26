﻿using System.Linq;
using JetBrains.Metadata.Reader.API;
using JetBrains.Metadata.Reader.Impl;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Util;
using ReSharper.Structured.Logging.Extensions;
using ReSharper.Structured.Logging.Highlighting;
using ReSharper.Structured.Logging.Serilog.Parsing;

namespace ReSharper.Structured.Logging.Analyzer
{
    [ElementProblemAnalyzer(typeof(IInvocationExpression))]
    public class ComplexObjectDestructureAnalyzer : ElementProblemAnalyzer<IInvocationExpression>
    {
        private static readonly IClrTypeName GenericDictionaryFqn = new ClrTypeName("System.Collections.Generic.Dictionary`2");

        private readonly MessageTemplateParser _messageTemplateParser;

        public ComplexObjectDestructureAnalyzer(MessageTemplateParser messageTemplateParser)
        {
            _messageTemplateParser = messageTemplateParser;
        }

        protected override void Run(
            IInvocationExpression element,
            ElementProblemAnalyzerData data,
            IHighlightingConsumer consumer)
        {
            var templateArgument = element.GetTemplateArgument();
            if (templateArgument == null)
            {
                return;
            }

            var complexObject = element.ArgumentList.Arguments
                .Where(CheckIfDestructureNeeded)
                .ToArray();
            if (complexObject.Length == 0)
            {
                return;
            }

            var stringLiteral = StringLiteralAltererUtil.TryCreateStringLiteralByExpression(templateArgument.Value);
            if (stringLiteral == null)
            {
                return;
            }

            var messageTemplate = _messageTemplateParser.Parse(stringLiteral.Expression.GetUnquotedText());
            if (messageTemplate.NamedProperties == null)
            {
                return;
            }

            var templateArgumentIndex = templateArgument.IndexOf();
            foreach (var argument in complexObject)
            {
                var index = argument.IndexOf() - templateArgumentIndex - 1;
                if (index < messageTemplate.NamedProperties.Length)
                {
                    var namedProperty = messageTemplate.NamedProperties[index];
                    if (namedProperty.Destructuring != Destructuring.Default)
                    {
                        continue;
                    }

                    consumer.AddHighlighting(new ComplexObjectDestructuringWarning(stringLiteral, namedProperty, stringLiteral.GetTokenDocumentRange(namedProperty)));
                }
            }
        }

        private static bool CheckIfDestructureNeeded(ICSharpArgument argument)
        {
            bool CheckIfBaseToStringUsed(IType type)
            {
                if (type.IsObject())
                {
                    return false;
                }

                if (type.IsPredefinedNumeric())
                {
                    return false;
                }

                if (type.IsString())
                {
                    return false;
                }

                if (type.IsGuid())
                {
                    return false;
                }

                var classType = type.GetClassType();
                if (classType == null)
                {
                    return false;
                }

                if (classType.Methods.Any(m => m.IsOverridesObjectToString()))
                {
                    return false;
                }

                return true;
            }

            // ReSharper disable once StyleCop.SA1305
            var iType = argument.GetExpressionType().ToIType();
            if (iType == null)
            {
                return false;
            }

            if (iType.IsNullable())
            {
                var nullable = iType.GetNullableUnderlyingType();
                if (nullable == null)
                {
                    return false;
                }

                return CheckIfBaseToStringUsed(nullable);
            }

            if (iType is IDeclaredType declaredType)
            {
                if (Equals(declaredType.GetClrName(), GenericDictionaryFqn))
                {
                    var argumentType = declaredType.GetFirstGenericArgumentType();

                    return argumentType != null && CheckIfBaseToStringUsed(argumentType);
                }

                var genericType = CollectionTypeUtil.GetElementTypesForGenericEnumerable(declaredType, false);
                if (genericType.Count == 1)
                {
                    return CheckIfBaseToStringUsed(genericType.Single());
                }
            }

            return CheckIfBaseToStringUsed(iType);
        }
    }
}
