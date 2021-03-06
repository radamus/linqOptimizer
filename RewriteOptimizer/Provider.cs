﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace OptimizableLINQ
{
    class OptimizerProvider : IQueryProvider
    {

        IQueryProvider wrappedProvider;

        internal OptimizerProvider()
        {

        }

        internal OptimizerProvider(IQueryProvider wrappedProvider)
        {
            this.wrappedProvider = wrappedProvider;
        }

        IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(System.Linq.Expressions.Expression expression)
        {
            return new OptimizableQueryable<TElement>(expression);

        }

        IQueryable IQueryProvider.CreateQuery(System.Linq.Expressions.Expression expression)
        {
            return new OptimizableQueryable<object>(expression);
        }

        TResult IQueryProvider.Execute<TResult>(System.Linq.Expressions.Expression expression)
        {


            bool IsEnumerable = (typeof(TResult).Name == typeof(IEnumerable<>).Name);


            Expression result = new Rewriter().Optimize(expression);
            if (wrappedProvider != null)
            {
                return wrappedProvider.Execute<TResult>(result);
            }

            LambdaModifier lm = new LambdaModifier();
            var result2 = lm.Modify((Expression) result);

            Func<TResult> func = Expression.Lambda<Func<TResult>>(result2).Compile();
            
            return func();
        }

        object IQueryProvider.Execute(System.Linq.Expressions.Expression expression)
        {

            Expression result = new Rewriter().Optimize(expression);
            if (wrappedProvider != null)
            {
                return wrappedProvider.Execute(result);
            }

            LambdaModifier lm = new LambdaModifier();
            var result2 = lm.Modify((Expression) result);

            return Expression.Lambda(result2).Compile().DynamicInvoke();

        }

    }

    public static class OptimizableExtension
    {
        public static IQueryable<TData> AsOptimizable<TData>(this IQueryable<TData> query)
        {
            if (query is OptimizableQueryable<TData>)
            {
                return query;
            }

            return new OptimizableQueryable<TData>(query);
        }

        public static IQueryable<TData> AsOptimizable<TData>(this IEnumerable<TData> query)
        {
            return new OptimizableQueryable<TData>(query.AsQueryable<TData>().Expression);
        }

        public static IQueryable AsOptimizable(this IEnumerable query)
        {
            return new OptimizableQueryable<object>(query.AsQueryable().Expression);
        }
    }

    
    internal class OptimizableQueryable<TData> : IQueryable<TData>, IQueryable, IOrderedQueryable<TData>, IOrderedQueryable
    {
        IQueryable<TData> queryable;
        Expression expression;

        OptimizerProvider provider;
        public Type ElementType { get { return typeof(TData); } }
        public Expression Expression { get { Expression expr =  expression == null ? queryable.Expression : expression; return expr; } private set { expression = value; } }
        public IQueryProvider Provider { get { return provider; } }

        IEnumerable<TData> enumerable;

        internal OptimizableQueryable(IQueryable<TData> queryable)
        {
            this.queryable = queryable;
            Expression = (Expression)null;

            provider = new OptimizerProvider(queryable.Provider);

        }

        internal OptimizableQueryable(Expression expression)
        {
            Expression = expression;
            provider = new OptimizerProvider();

        }

        IEnumerator<TData> IEnumerable<TData>.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        IEnumerator<TData> GetEnumerator() {
            if (enumerable == null) 
                enumerable = Provider.Execute<IEnumerable<TData>>(Expression);
            
            return enumerable.GetEnumerator();
        }

    }

    public class LambdaModifier : ExpressionVisitor
    {
        public Expression Modify(Expression expression)
        {
            return Visit(expression);
        }

  /*      protected override Expression VisitLambda<T>(Expression<T> e)
        {
            LambdaExpression lambda = (LambdaExpression) e;
            Expression body = this.Visit(lambda.Body);            
            LambdaExpression l = Expression.Lambda(lambda.Type, body, lambda.Parameters);
            var compiled = l.Compile();
            Expression c = Expression.Constant(compiled);
            return c;
        }/**/
        
        protected override Expression VisitMethodCall(MethodCallExpression mce)
                {
                    bool hasLambdas = false;
            
                    IList<Expression> args = new List<Expression>();
                    foreach(Expression e in mce.Arguments) {

                        if (e.NodeType == ExpressionType.Quote)
                        {
                            UnaryExpression u = (UnaryExpression) this.Visit(e);
                            args.Add(this.Visit(e));
                        } else if (e.NodeType == ExpressionType.Lambda)
                        {
                            LambdaExpression l = (LambdaExpression) this.Visit(e);
                            Expression c = Expression.Constant(l.Compile());
                            args.Add(c);

                            hasLambdas = true;
                        }
                        else
                            args.Add(this.Visit(e));
                    
                    }

                    if (hasLambdas)
                    {
                        return Expression.Call(mce.Method, args);
                    }
            
                    return base.VisitMethodCall(mce);
                }/**/
    }
}
