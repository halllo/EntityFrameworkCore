// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Newtonsoft.Json.Linq;

namespace Microsoft.EntityFrameworkCore.Cosmos.Query.Internal
{
    public partial class CosmosShapedQueryCompilingExpressionVisitor
    {
        private sealed class QueryingEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>
        {
            private readonly CosmosQueryContext _cosmosQueryContext;
            private readonly ISqlExpressionFactory _sqlExpressionFactory;
            private readonly SelectExpression _selectExpression;
            private readonly Func<QueryContext, JObject, T> _shaper;
            private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
            private readonly Type _contextType;
            private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

            public QueryingEnumerable(
                CosmosQueryContext cosmosQueryContext,
                ISqlExpressionFactory sqlExpressionFactory,
                IQuerySqlGeneratorFactory querySqlGeneratorFactory,
                SelectExpression selectExpression,
                Func<QueryContext, JObject, T> shaper,
                Type contextType,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            {
                _cosmosQueryContext = cosmosQueryContext;
                _sqlExpressionFactory = sqlExpressionFactory;
                _querySqlGeneratorFactory = querySqlGeneratorFactory;
                _selectExpression = selectExpression;
                _shaper = shaper;
                _contextType = contextType;
                _logger = logger;
            }

            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                => new AsyncEnumerator(this, cancellationToken);

            public IEnumerator<T> GetEnumerator() => new Enumerator(this);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator : IEnumerator<T>
            {
                private IEnumerator<JObject> _enumerator;
                private readonly CosmosQueryContext _cosmosQueryContext;
                private readonly SelectExpression _selectExpression;
                private readonly Func<QueryContext, JObject, T> _shaper;
                private readonly ISqlExpressionFactory _sqlExpressionFactory;
                private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
                private readonly Type _contextType;
                private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

                public Enumerator(QueryingEnumerable<T> queryingEnumerable)
                {
                    _cosmosQueryContext = queryingEnumerable._cosmosQueryContext;
                    _shaper = queryingEnumerable._shaper;
                    _selectExpression = queryingEnumerable._selectExpression;
                    _sqlExpressionFactory = queryingEnumerable._sqlExpressionFactory;
                    _querySqlGeneratorFactory = queryingEnumerable._querySqlGeneratorFactory;
                    _contextType = queryingEnumerable._contextType;
                    _logger = queryingEnumerable._logger;
                }

                public T Current { get; private set; }

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    try
                    {
                        using (_cosmosQueryContext.ConcurrencyDetector.EnterCriticalSection())
                        {
                            if (_enumerator == null)
                            {
                                var selectExpression = (SelectExpression)new InExpressionValuesExpandingExpressionVisitor(
                                    _sqlExpressionFactory, _cosmosQueryContext.ParameterValues).Visit(_selectExpression);

                                var sqlQuery = _querySqlGeneratorFactory.Create().GetSqlQuery(
                                    selectExpression, _cosmosQueryContext.ParameterValues);

                                _enumerator = _cosmosQueryContext.CosmosClient
                                    .ExecuteSqlQuery(
                                        _selectExpression.Container,
                                        sqlQuery)
                                    .GetEnumerator();
                            }

                            var hasNext = _enumerator.MoveNext();

                            Current
                                = hasNext
                                    ? _shaper(_cosmosQueryContext, _enumerator.Current)
                                    : default;

                            return hasNext;
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.QueryIterationFailed(_contextType, exception);

                        throw;
                    }
                }

                public void Dispose()
                {
                    _enumerator?.Dispose();
                    _enumerator = null;
                }

                public void Reset() => throw new NotImplementedException();
            }

            private sealed class AsyncEnumerator : IAsyncEnumerator<T>
            {
                private IAsyncEnumerator<JObject> _enumerator;
                private readonly CosmosQueryContext _cosmosQueryContext;
                private readonly SelectExpression _selectExpression;
                private readonly Func<QueryContext, JObject, T> _shaper;
                private readonly ISqlExpressionFactory _sqlExpressionFactory;
                private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
                private readonly Type _contextType;
                private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;
                private readonly CancellationToken _cancellationToken;

                public AsyncEnumerator(QueryingEnumerable<T> queryingEnumerable, CancellationToken cancellationToken)
                {
                    _cosmosQueryContext = queryingEnumerable._cosmosQueryContext;
                    _shaper = queryingEnumerable._shaper;
                    _selectExpression = queryingEnumerable._selectExpression;
                    _sqlExpressionFactory = queryingEnumerable._sqlExpressionFactory;
                    _querySqlGeneratorFactory = queryingEnumerable._querySqlGeneratorFactory;
                    _contextType = queryingEnumerable._contextType;
                    _logger = queryingEnumerable._logger;
                    _cancellationToken = cancellationToken;
                }

                public T Current { get; private set; }

                public async ValueTask<bool> MoveNextAsync()
                {
                    try
                    {
                        using (_cosmosQueryContext.ConcurrencyDetector.EnterCriticalSection())
                        {
                            if (_enumerator == null)
                            {
                                var selectExpression = (SelectExpression)new InExpressionValuesExpandingExpressionVisitor(
                                    _sqlExpressionFactory, _cosmosQueryContext.ParameterValues).Visit(_selectExpression);

                                _enumerator = _cosmosQueryContext.CosmosClient
                                    .ExecuteSqlQueryAsync(
                                        _selectExpression.Container,
                                        _querySqlGeneratorFactory.Create().GetSqlQuery(
                                            selectExpression, _cosmosQueryContext.ParameterValues))
                                    .GetAsyncEnumerator(_cancellationToken);
                            }

                            var hasNext = await _enumerator.MoveNextAsync();

                            Current
                                = hasNext
                                    ? _shaper(_cosmosQueryContext, _enumerator.Current)
                                    : default;

                            return hasNext;
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.QueryIterationFailed(_contextType, exception);

                        throw;
                    }
                }

                public ValueTask DisposeAsync()
                {
                    _enumerator?.DisposeAsync();
                    _enumerator = null;

                    return default;
                }
            }
        }
    }
}
