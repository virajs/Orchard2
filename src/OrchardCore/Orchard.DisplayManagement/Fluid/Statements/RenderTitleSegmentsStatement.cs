﻿using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Fluid;
using Fluid.Ast;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.DependencyInjection;
using Orchard.DisplayManagement.Fluid.Ast;
using Orchard.DisplayManagement.Title;

namespace Orchard.DisplayManagement.Fluid.Statements
{
    public class RenderTitleSegmentsStatement : Statement
    {
        private readonly ArgumentsExpression _arguments;

        public RenderTitleSegmentsStatement(Expression segment, ArgumentsExpression arguments)
        {
            Segment = segment;
            _arguments = arguments;
        }

        public Expression Segment { get; }

        public override async Task<Completion> WriteToAsync(TextWriter writer, TextEncoder encoder, TemplateContext context)
        {
            if (!context.AmbientValues.TryGetValue("Services", out var services))
            {
                throw new ArgumentException("Services missing while invoking 'page_title'");
            }

            var titleBuilder = ((IServiceProvider)services).GetRequiredService<IPageTitleBuilder>();

            var segment = new HtmlString((await Segment.EvaluateAsync(context)).ToStringValue());

            var arguments = _arguments == null ? new FilterArguments()
                : (FilterArguments)(await _arguments.EvaluateAsync(context)).ToObjectValue();

            var position = arguments.HasNamed("position") ? arguments["position"].ToStringValue() : "0";
            var separator = arguments.HasNamed("separator") ? new HtmlString(arguments["separator"].ToStringValue()) : null;

            titleBuilder.AddSegment(segment, position);
            titleBuilder.GenerateTitle(separator).WriteTo(writer, HtmlEncoder.Default);
            return Completion.Normal;
        }
    }
}