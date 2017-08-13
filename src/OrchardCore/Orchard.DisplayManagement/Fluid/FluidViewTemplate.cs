﻿using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Fluid;
using Fluid.Accessors;
using Fluid.Values;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Orchard.DisplayManagement.Fluid.Internal;
using Orchard.DisplayManagement.Implementation;
using Orchard.DisplayManagement.Layout;
using Orchard.DisplayManagement.Shapes;
using Orchard.Liquid;
using Orchard.Settings;

namespace Orchard.DisplayManagement.Fluid
{
    public class FluidViewTemplate : FluidTemplate<ActivatorFluidParserFactory<FluidViewParser>>
    {
        public static readonly string ViewsFolder = "Views";
        public static readonly string ViewExtension = ".liquid";
        public static readonly MemoryCache Cache = new MemoryCache(new MemoryCacheOptions());

        internal static async Task RenderAsync(FluidPage page)
        {
            var services = page.Context.RequestServices;
            var path = Path.ChangeExtension(page.ViewContext.ExecutingFilePath, ViewExtension);
            var fileProviderAccessor = services.GetRequiredService<IFluidViewFileProviderAccessor>();
            var template = Parse(path, fileProviderAccessor.FileProvider, Cache);

            var displayHelper = services.GetRequiredService<IDisplayHelperFactory>().CreateHelper(page.ViewContext);

            var context = new TemplateContext();

            context.Contextualize(new DisplayContext()
            {
                ServiceProvider = page.Context.RequestServices,
                DisplayAsync = displayHelper,
                ViewContext = page.ViewContext,
                Value = page.Model
            });

            var liquidOptions = services.GetRequiredService<IOptions<LiquidOptions>>().Value;

            foreach (var registration in liquidOptions.FilterRegistrations)
            {
                context.Filters.AddAsyncFilter(registration.Key, (input, arguments, ctx) =>
                {
                    var type = registration.Value;
                    var filter = services.GetRequiredService(registration.Value) as ILiquidFilter;
                    return filter.ProcessAsync(input, arguments, ctx);
                });
            }

            page.WriteLiteral(await template.RenderAsync(context));
        }

        public static IFluidTemplate Parse(string path, IFileProvider fileProvider, IMemoryCache cache)
        {
            return cache.GetOrCreate(path, entry =>
            {
                entry.Priority = CacheItemPriority.NeverRemove;
                var fileInfo = fileProvider.GetFileInfo(path);
                entry.ExpirationTokens.Add(fileProvider.Watch(path));

                using (var stream = fileInfo.CreateReadStream())
                {
                    using (var sr = new StreamReader(stream))
                    {
                        if (TryParse(sr.ReadToEnd(), out var template, out var errors))
                        {
                            return template;
                        }
                        else
                        {
                            throw new Exception(String.Join(System.Environment.NewLine, errors));
                        }
                    }
                }
            });
        }
    }

    public static class TemplateContextExtensions
    {
        public static async void Contextualize(this TemplateContext context, DisplayContext displayContext)
        {
            var services = displayContext.ServiceProvider;
            context.AmbientValues.Add("Services", services);

            var displayHelperFactory = services.GetRequiredService<IDisplayHelperFactory>();
            context.AmbientValues.Add("DisplayHelperFactory", displayHelperFactory);

            context.AmbientValues.Add("DisplayHelper", displayContext.DisplayAsync);
            context.AmbientValues.Add("ViewContext", displayContext.ViewContext);

            var urlHelperFactory = services.GetRequiredService<IUrlHelperFactory>();
            var urlHelper = urlHelperFactory.GetUrlHelper(displayContext.ViewContext);
            context.AmbientValues.Add("UrlHelper", urlHelper);

            var shapeFactory = services.GetRequiredService<IShapeFactory>();
            context.AmbientValues.Add("ShapeFactory", shapeFactory);

            var localizer = services.GetRequiredService<IViewLocalizer>();
            var contextable = localizer as IViewContextAware;

            if (contextable != null)
            {
                contextable.Contextualize(displayContext.ViewContext);
            }

            context.AmbientValues.Add("ViewLocalizer", localizer);

            var layoutAccessor = services.GetRequiredService<ILayoutAccessor>();
            context.AmbientValues.Add("LayoutAccessor", layoutAccessor);

            var layout = layoutAccessor.GetLayout();
            context.AmbientValues.Add("ThemeLayout", layout);

            var site = await services.GetRequiredService<ISiteService>().GetSiteSettingsAsync();
            context.MemberAccessStrategy.Register(site.GetType());
            context.LocalScope.SetValue("Site", site);

            var model = displayContext.Value as dynamic;
            context.RegisterObject((object)model);
            context.LocalScope.SetValue("Model", model);

            if (model is Shape)
            {
                if (model.Properties.Count > 0)
                {
                    foreach (var prop in model.Properties)
                    {
                        context.RegisterObject((object)prop.Value);
                    }
                }
            }

            if (((object)model).GetType().GetProperty("Field") != null)
            {
                context.RegisterObject(((object)model.Field));
            }
        }

        public static void RegisterObject(this TemplateContext context, object obj)
        {
            var type = obj.GetType();

            if (obj is IShape && !FluidValue.TypeMappings.TryGetValue(type, out var value))
            {
                FluidValue.TypeMappings.Add(type, o => new ObjectValue(o));

                if (obj is Shape)
                {
                    TemplateContext.GlobalMemberAccessStrategy.Register(type, "*", new DelegateAccessor((o, n) =>
                    {
                        if ((o as Shape).Properties.TryGetValue(n, out object result))
                        {
                            return result;
                        }

                        foreach (var item in (o as Shape).Items)
                        {
                            if (item is IShape && item.Metadata.Type == n)
                            {
                                return item;
                            }
                        }

                        return null;
                    }));
                }
            }

            context.MemberAccessStrategy.Register(type);
        }
    }
}