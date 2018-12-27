﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Unity.Builder;
using Unity.Container.Lifetime;

namespace Unity.Processors
{
    public class ConstructorProcessor : MethodBaseInfoProcessor<ConstructorInfo>
    {
        #region Fields

        private static readonly ConstructorLengthComparer ConstructorComparer = new ConstructorLengthComparer();

        private static readonly ConstructorInfo PerResolveInfo = typeof(InternalPerResolveLifetimeManager)
            .GetTypeInfo().DeclaredConstructors.First();

        private static readonly ConstructorInfo InvalidOperationExceptionCtor =
            typeof(InvalidOperationException)
                .GetTypeInfo()
                .DeclaredConstructors
                .First(c =>
                {
                    var parameters = c.GetParameters();
                    return 2 == parameters.Length &&
                           typeof(string) == parameters[0].ParameterType &&
                           typeof(Exception) == parameters[1].ParameterType;
                });

        private static readonly Expression NoConstructorExceptionExpr =
            Expression.Throw(
                Expression.New(InvalidOperationExceptionCtor,
                    Expression.Call(StringFormat,
                        Expression.Constant("No public constructor is available for type {0}."),
                        BuilderContextExpression.Type),
                    InvalidRegistrationExpression));

        #endregion


        #region Constructors

        public ConstructorProcessor()
            : base(typeof(InjectionConstructorAttribute))
        {

        }

        #endregion


        #region Overrides

        protected override ConstructorInfo[] DeclaredMembers(Type type)
        {
#if NETSTANDARD1_0
            return type.GetTypeInfo()
                       .DeclaredConstructors
                       .Where(c => c.IsStatic == false && c.IsPublic)
                       .ToArray();
#else
            return type.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                       .ToArray();
#endif
        }

        /// <summary>
        /// Selects default constructor
        /// </summary>
        /// <param name="type"><see cref="Type"/> to be built</param>
        /// <param name="members">All public constructors this type implements</param>
        /// <returns></returns>
        protected override object GetDefault(Type type, ConstructorInfo[] members)
        {
            Array.Sort(members, ConstructorComparer);

            switch (members.Length)
            {
                case 0:
                    return null;

                case 1:
                    return members[0];

                default:
                    var paramLength = members[0].GetParameters().Length;
                    if (members[1].GetParameters().Length == paramLength)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                CultureInfo.CurrentCulture,
                                Constants.AmbiguousInjectionConstructor,
                                type.GetTypeInfo().Name,
                                paramLength));
                    }
                    return members[0];
            }
        }

        public override IEnumerable<Expression> GetEnumerator(ref BuilderContext context)
        {
            // Verify the type we're trying to build is actually constructable -
            // CLR primitive types like string and int aren't.
#if NETSTANDARD1_0 || NETCOREAPP1_0
            if (!context.Type.GetTypeInfo().IsInterface)
#else
            if (!context.Type.IsInterface)
#endif
            {
                if (context.Type == typeof(string))
                {
                    throw new InvalidOperationException(
                        $"The type {context.Type.Name} cannot be constructed. You must configure the container to supply this value.");
                }
            }

            var newExpr = base.GetEnumerator(ref context)
                                 .FirstOrDefault() ?? 
                          NoConstructorExceptionExpr;

            var IfThenExpr = Expression.IfThen(Expression.Equal(Expression.Constant(null), BuilderContextExpression.Existing),
                    ValidateConstructedType(ref context) ?? newExpr);

            return context.Registration.Get(typeof(LifetimeManager)) is PerResolveLifetimeManager
                ? new[] { IfThenExpr, BuilderContextExpression.Set(context.RegistrationType, 
                                                                   context.RegistrationName, 
                                                                   typeof(LifetimeManager),
                                                                   Expression.New(PerResolveInfo, 
                                                                                  BuilderContextExpression.Existing)) }
                : new Expression[] { IfThenExpr };
        }

        protected override Expression ValidateMemberInfo(ConstructorInfo info)
        {
            var parameters = info.GetParameters();
            if (parameters.Any(pi => pi.ParameterType.IsByRef))
            {
                return Expression.Throw(Expression.New(InvalidOperationExceptionCtor,
                        Expression.Constant(CreateErrorMessage(Constants.SelectedConstructorHasRefParameters, info.DeclaringType, info)),
                        InvalidRegistrationExpression));
            }

            return null;

            // TODO: Check if required
            string CreateErrorMessage(string format, Type type, MethodBase constructor)
            {
                var parameterDescriptions =
                    constructor.GetParameters()
                        .Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}");

                return string.Format(format, type.FullName, string.Join(", ", parameterDescriptions));
            }

        }

        protected override Expression CreateExpression(ConstructorInfo info, object[] resolvers)
        {
            var variable = Expression.Variable(info.DeclaringType);
            return Expression.Block(new[] { variable }, new Expression[]
            {
                Expression.Assign(variable, Expression.New(info, CreateParameterExpressions(info.GetParameters(), resolvers))),
                Expression.Assign(BuilderContextExpression.Existing, Expression.Convert(variable, typeof(object))) 
            });
        }

        #endregion


        #region Implementation

        private Expression ValidateConstructedType(ref BuilderContext context)
        {
#if NETSTANDARD1_0 || NETCOREAPP1_0
            var typeInfo = context.Type.GetTypeInfo();
            if (typeInfo.IsInterface)
#else
            if (context.Type.IsInterface)
#endif
            {
                return Expression.Throw(
                    Expression.New(InvalidOperationExceptionCtor,
                        Expression.Call(
                            StringFormat,
                            Expression.Constant(Constants.CannotConstructInterface),
                            BuilderContextExpression.Type),
                        InvalidRegistrationExpression));
            }

#if NETSTANDARD1_0 || NETCOREAPP1_0
            if (typeInfo.IsAbstract)
#else
            if (context.Type.IsAbstract)
#endif
            {
                return Expression.Throw(
                    Expression.New(InvalidOperationExceptionCtor,
                        Expression.Call(
                            StringFormat,
                            Expression.Constant(Constants.CannotConstructAbstractClass),
                            BuilderContextExpression.Type),
                        InvalidRegistrationExpression));
            }

#if NETSTANDARD1_0 || NETCOREAPP1_0
            if (typeInfo.IsSubclassOf(typeof(Delegate)))
#else
            if (context.Type.IsSubclassOf(typeof(Delegate)))
#endif
            {
                return Expression.Throw(
                    Expression.New(InvalidOperationExceptionCtor,
                        Expression.Call(
                            StringFormat,
                            Expression.Constant(Constants.CannotConstructDelegate),
                            BuilderContextExpression.Type),
                        InvalidRegistrationExpression));
            }

            if (context.Type == typeof(string))
            {
                return Expression.Throw(
                    Expression.New(InvalidOperationExceptionCtor,
                        Expression.Call(
                            StringFormat,
                            Expression.Constant(Constants.TypeIsNotConstructable),
                            BuilderContextExpression.Type),
                        InvalidRegistrationExpression));
            }

            return null;
        }

        #endregion


        #region Nested Types

        private class ConstructorLengthComparer : IComparer<ConstructorInfo>
        {
            public int Compare(ConstructorInfo x, ConstructorInfo y) => y?.GetParameters().Length ?? 0 - x?.GetParameters().Length ?? 0;
        }

        #endregion
    }
}
