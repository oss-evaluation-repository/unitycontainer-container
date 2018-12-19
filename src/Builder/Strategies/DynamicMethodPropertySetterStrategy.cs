﻿using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Unity.Builder.Expressions;
using Unity.Injection;
using Unity.ObjectBuilder.BuildPlan.DynamicMethod;
using Unity.Policy;

namespace Unity.Builder.Strategies
{
    /// <summary>
    /// A <see cref="BuilderStrategy"/> that generates IL to resolve properties
    /// on an object being built.
    /// </summary>
    public class DynamicMethodPropertySetterStrategy : BuilderStrategy// CompiledStrategy<PropertyInfo, object>
    {
        #region BuilderStrategy

        /// <summary>
        /// Called during the chain of responsibility for a build operation.
        /// </summary>
        /// <param name="context">The context for the operation.</param>
        public override void PreBuildUp(ref BuilderContext context)
        {
            var dynamicBuildContext = (DynamicBuildPlanGenerationContext)context.Existing;

            var selector = context.GetPolicy<IPropertySelectorPolicy>(context.OriginalBuildKey.Type, 
                                                                      context.OriginalBuildKey.Name);

            foreach (var property in selector.SelectProperties(ref context))
            {
                ParameterExpression resolvedObjectParameter;

                switch (property)
                {
                    case PropertyInfo propertyInfo:
                        resolvedObjectParameter = Expression.Parameter(propertyInfo.PropertyType);

                        dynamicBuildContext.AddToBuildPlan(
                            Expression.Block(
                                new[] { resolvedObjectParameter },
                                Expression.Assign(
                                    resolvedObjectParameter,
                                    BuilderContextExpression.Resolve(propertyInfo, 
                                                                     context.OriginalBuildKey.Name, 
                                                                     AttributeResolverFactory.CreateResolver(propertyInfo))),
                                Expression.Call(
                                    Expression.Convert(
                                        BuilderContextExpression.Existing,
                                        dynamicBuildContext.TypeToBuild),
                                    GetValidatedPropertySetter(propertyInfo),
                                    resolvedObjectParameter)));
                        break;

                    case SelectedProperty selectedProperty:
                        resolvedObjectParameter = Expression.Parameter(selectedProperty.Property.PropertyType);
                                                                                                                                                            
                        dynamicBuildContext.AddToBuildPlan(
                            Expression.Block(
                                new[] { resolvedObjectParameter },
                                Expression.Assign(
                                    resolvedObjectParameter,
                                    BuilderContextExpression.Resolve(selectedProperty.Property, 
                                                                     context.OriginalBuildKey.Name, 
                                                                     selectedProperty.Resolver)),
                                Expression.Call(
                                    Expression.Convert(
                                        BuilderContextExpression.Existing,
                                        dynamicBuildContext.TypeToBuild),
                                    GetValidatedPropertySetter(selectedProperty.Property),
                                    resolvedObjectParameter)));
                        break;

                    case InjectionProperty injectionProperty:
                        var (info, value) = injectionProperty.FromType(context.Type);
                        resolvedObjectParameter = Expression.Parameter(info.PropertyType);

                        dynamicBuildContext.AddToBuildPlan(
                            Expression.Block(
                                new[] { resolvedObjectParameter },
                                Expression.Assign(
                                    resolvedObjectParameter,
                                    BuilderContextExpression.Resolve(info, 
                                                                     context.OriginalBuildKey.Name, 
                                                                     value)),
                                Expression.Call(
                                    Expression.Convert(
                                        BuilderContextExpression.Existing,
                                        dynamicBuildContext.TypeToBuild),
                                    GetValidatedPropertySetter(info),
                                    resolvedObjectParameter)));
                        break;

                    default:
                        throw new InvalidOperationException("Unknown type of property");
                }
            }
        }

        #endregion


        #region Implementation

        private static MethodInfo GetValidatedPropertySetter(PropertyInfo property)
        {
            // TODO: Check - Added a check for private to meet original expectations;
            var setter = property.GetSetMethod(true);
            if (setter == null || setter.IsPrivate)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                    Constants.PropertyNotSettable, property.Name, property.DeclaringType?.FullName));
            }
            return setter;
        }

        #endregion
    }
}
