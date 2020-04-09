// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Utilities;
using Newtonsoft.Json.Linq;

namespace Microsoft.EntityFrameworkCore.Cosmos.Query.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public partial class CosmosShapedQueryCompilingExpressionVisitor : ShapedQueryCompilingExpressionVisitor
    {
        private readonly ISqlExpressionFactory _sqlExpressionFactory;
        private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
        private readonly Type _contextType;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;
        private readonly string _partitionKey;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public CosmosShapedQueryCompilingExpressionVisitor(
            [NotNull] ShapedQueryCompilingExpressionVisitorDependencies dependencies,
            [NotNull] CosmosQueryCompilationContext cosmosQueryCompilationContext,
            [NotNull] ISqlExpressionFactory sqlExpressionFactory,
            [NotNull] IQuerySqlGeneratorFactory querySqlGeneratorFactory)
            : base(dependencies, cosmosQueryCompilationContext)
        {
            _sqlExpressionFactory = sqlExpressionFactory;
            _querySqlGeneratorFactory = querySqlGeneratorFactory;
            _contextType = cosmosQueryCompilationContext.ContextType;
            _logger = cosmosQueryCompilationContext.Logger;
            _partitionKey = cosmosQueryCompilationContext.PartitionKey;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        protected override Expression VisitShapedQueryExpression(ShapedQueryExpression shapedQueryExpression)
        {
            Check.NotNull(shapedQueryExpression, nameof(shapedQueryExpression));

            var selectExpression = (SelectExpression)shapedQueryExpression.QueryExpression;
            selectExpression.ApplyProjection();
            var jObjectParameter = Expression.Parameter(typeof(JObject), "jObject");

            var shaperBody = shapedQueryExpression.ShaperExpression;
            shaperBody = new JObjectInjectingExpressionVisitor()
                .Visit(shaperBody);
            shaperBody = InjectEntityMaterializers(shaperBody);
            shaperBody = new CosmosProjectionBindingRemovingExpressionVisitor(selectExpression, jObjectParameter, QueryCompilationContext.IsTracking)
                .Visit(shaperBody);

            var shaperLambda = Expression.Lambda(
                shaperBody,
                QueryCompilationContext.QueryContextParameter,
                jObjectParameter);

            return Expression.New(
                typeof(QueryingEnumerable<>).MakeGenericType(shaperLambda.ReturnType).GetConstructors()[0],
                Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(CosmosQueryContext)),
                Expression.Constant(_sqlExpressionFactory),
                Expression.Constant(_querySqlGeneratorFactory),
                Expression.Constant(selectExpression),
                Expression.Constant(shaperLambda.Compile()),
                Expression.Constant(_contextType),
                Expression.Constant(_partitionKey, typeof(string)),
                Expression.Constant(_logger));
        }
    }
}
