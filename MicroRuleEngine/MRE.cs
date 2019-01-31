using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

// ReSharper disable UnusedMember.Global

namespace MicroRuleEngine
{
    public class MRE
    {
        private static readonly Lazy<MethodInfo> MiGetItem = new Lazy<MethodInfo>(() =>
                                                                                      typeof(DataRow).GetMethod("get_Item",
                                                                                                                new[]
                                                                                                                {
                                                                                                                    typeof
                                                                                                                        (string
                                                                                                                        )
                                                                                                                }));

        private static readonly Lazy<MethodInfo> MiRegexIsMatch = new Lazy<MethodInfo>(() =>
                                                                                           typeof(Regex).GetMethod("IsMatch",
                                                                                                                   new[]
                                                                                                                   {
                                                                                                                       typeof
                                                                                                                           (string
                                                                                                                           ),
                                                                                                                       typeof
                                                                                                                           (string
                                                                                                                           ),
                                                                                                                       typeof
                                                                                                                           (RegexOptions
                                                                                                                           )
                                                                                                                   }));

        private static readonly ExpressionType[] NestedOperators =
            {ExpressionType.And, ExpressionType.AndAlso, ExpressionType.Or, ExpressionType.OrElse};

        private static readonly Tuple<string, Lazy<MethodInfo>>[] EnumerableMethodsByName =
        {
            Tuple.Create("Any",
                         new Lazy<MethodInfo>(() => GetLinqMethod("Any",
                                                                  2))),
            Tuple.Create("All",
                         new Lazy<MethodInfo>(() => GetLinqMethod("All",
                                                                  2)))
        };

        private static readonly Lazy<MethodInfo> MiDecimalTryParse = new Lazy<MethodInfo>(() =>
                                                                                              typeof(decimal)
                                                                                                  .GetMethod("TryParse",
                                                                                                             new[]
                                                                                                             {
                                                                                                                 typeof
                                                                                                                     (string
                                                                                                                     ),
                                                                                                                 Type
                                                                                                                     .GetType("System.Decimal&")
                                                                                                             }));

        private static readonly Lazy<MethodInfo> MiDoubleTryParse = new Lazy<MethodInfo>(() =>
                                                                                             typeof(double)
                                                                                                 .GetMethod("TryParse",
                                                                                                            new[]
                                                                                                            {
                                                                                                                typeof(
                                                                                                                    string
                                                                                                                    ),
                                                                                                                Type
                                                                                                                    .GetType("System.Double&")
                                                                                                            }));

        private static readonly Lazy<MethodInfo> MiFloatTryParse = new Lazy<MethodInfo>(() =>
                                                                                            typeof(float)
                                                                                                .GetMethod("TryParse",
                                                                                                           new[]
                                                                                                           {
                                                                                                               typeof(
                                                                                                                   string
                                                                                                                   ),
                                                                                                               Type
                                                                                                                   .GetType("System.Single&")
                                                                                                           }));

        private static readonly Lazy<MethodInfo> MiIntTryParse = new Lazy<MethodInfo>(() =>
                                                                                          typeof(int).GetMethod("TryParse",
                                                                                                                new[]
                                                                                                                {
                                                                                                                    typeof
                                                                                                                        (string
                                                                                                                        ),
                                                                                                                    Type
                                                                                                                        .GetType("System.Int32&")
                                                                                                                }));

        private static readonly Regex RegexIndexed = new Regex(@"(\w+)\[(\d+)\]",
                                                               RegexOptions.Compiled);

        protected static Expression BinaryExpression(IEnumerable<Expression> expressions,
                                                     ExpressionType          operationType)
        {
            Func<Expression, Expression, Expression> methodExp;
            switch (operationType)
            {
                case ExpressionType.Or:
                    methodExp = Expression.Or;
                    break;
                case ExpressionType.OrElse:
                    methodExp = Expression.OrElse;
                    break;
                case ExpressionType.AndAlso:
                    methodExp = Expression.AndAlso;
                    break;
                default:
                    methodExp = Expression.And;
                    break;
            }

            return expressions.Aggregate(methodExp);
        }

        private static Expression BuildEnumerableOperatorExpression(Type                type,
                                                                    Rule                rule,
                                                                    ParameterExpression parameterExpression)
        {
            var collectionPropertyExpression = BuildExpr(type,
                                                         rule,
                                                         parameterExpression);

            var itemType            = GetCollectionItemType(collectionPropertyExpression.Type);
            var expressionParameter = Expression.Parameter(itemType);


            var genericFunc = typeof(Func<,>).MakeGenericType(itemType,
                                                              typeof(bool));

            var innerExp = BuildNestedExpression(itemType,
                                                 rule.Rules,
                                                 expressionParameter,
                                                 ExpressionType.And);

            var predicate = Expression.Lambda(genericFunc,
                                              innerExp,
                                              expressionParameter);

            var body = Expression.Call(typeof(Enumerable),
                                       rule.Operator,
                                       new[] {itemType},
                                       collectionPropertyExpression,
                                       predicate);

            return body;
        }

        private static Expression BuildExpr(Type       type,
                                            Rule       rule,
                                            Expression param)
        {
            Expression propExpression;
            Type       propType;

            if (param.Type == typeof(object))
            {
                param = Expression.TypeAs(param,
                                          type);
            }

            var dataRule = rule as DataRule;

            if (string.IsNullOrEmpty(rule.MemberName)) //check is against the object itself
            {
                propExpression = param;
                propType       = propExpression.Type;
            }
            else if (dataRule != null)
            {
                if (type != typeof(DataRow))
                {
                    throw new RulesException(" Bad rule");
                }

                propExpression = GetDataRowField(param,
                                                 dataRule.MemberName,
                                                 dataRule.Type);
                propType = propExpression.Type;
            }
            else
            {
                propExpression = GetProperty(param,
                                             rule.MemberName);
                propType = propExpression.Type;
            }

            //This caused issues passing the expression to EF.
            //With out this it lets us use the rules to build complex where clauses for EF queries.
            //propExpression = Expression.TryCatch(
            //	Expression.Block(propExpression.Type, propExpression),
            //	Expression.Catch(typeof(NullReferenceException), Expression.Default(propExpression.Type))
            //);
            // is the operator a known .NET operator?
            ExpressionType tBinary;

            if (Enum.TryParse(rule.Operator,
                              out tBinary))
            {
                Expression right;
                var        txt = rule.TargetValue as string;
                if (txt != null &&
                    txt.StartsWith("*."))
                {
                    txt = txt.Substring(2);
                    right = GetProperty(param,
                                        txt);
                }
                else
                {
                    right = StringToExpression(rule.TargetValue,
                                               propType);
                }

                return Expression.MakeBinary(tBinary,
                                             propExpression,
                                             right);
            }

            switch (rule.Operator)
            {
                case "IsMatch":
                    return Expression.Call(MiRegexIsMatch.Value,
                                           propExpression,
                                           Expression.Constant(rule.TargetValue,
                                                               typeof(string)),
                                           Expression.Constant(RegexOptions.IgnoreCase,
                                                               typeof(RegexOptions)));
                case "IsInteger":
                    return Expression.Call(MiIntTryParse.Value,
                                           propExpression,
                                           Expression.MakeMemberAccess(null,
                                                                       typeof(Placeholder).GetField("Int")));
                case "IsSingle":
                    return Expression.Call(MiFloatTryParse.Value,
                                           propExpression,
                                           Expression.MakeMemberAccess(null,
                                                                       typeof(Placeholder).GetField("Float")));
                case "IsDouble":
                    return Expression.Call(MiDoubleTryParse.Value,
                                           propExpression,
                                           Expression.MakeMemberAccess(null,
                                                                       typeof(Placeholder).GetField("Double")));
                case "IsDecimal":
                    return Expression.Call(MiDecimalTryParse.Value,
                                           propExpression,
                                           Expression.MakeMemberAccess(null,
                                                                       typeof(Placeholder).GetField("Decimal")));
            }

            var enumerableOperation = IsEnumerableOperator(rule.Operator);

            if (enumerableOperation != null)
            {
                var elementType = ElementType(propType);
                var lambdaParam = Expression.Parameter(elementType,
                                                       "lambdaParam");
                return rule.Rules?.Any() == true
                           ? Expression.Call(enumerableOperation.MakeGenericMethod(elementType),
                                             propExpression,
                                             Expression.Lambda(BuildNestedExpression(elementType,
                                                                                     rule.Rules,
                                                                                     lambdaParam,
                                                                                     ExpressionType.AndAlso),
                                                               lambdaParam))
                           : Expression.Call(enumerableOperation.MakeGenericMethod(elementType),
                                             propExpression);
            }

            var inputs = rule.Inputs.Select(x => x.GetType())
                             .ToArray();

            var methodInfo = propType.GetMethod(rule.Operator,
                                                inputs);
            if (methodInfo == null)
            {
                throw new RulesException($"'{rule.Operator}' is not a method of '{propType.Name}");
            }

            if (!methodInfo.IsGenericMethod)
            {
                inputs = null; //Only pass in type information to a Generic Method
            }

            var expressions = rule.Inputs.Select(Expression.Constant)
                                  .ToArray();

            return Expression.Call(propExpression,
                                   rule.Operator,
                                   inputs,
                                   expressions);
        }

        protected static Expression BuildNestedExpression(Type                type,
                                                          IEnumerable<Rule>   rules,
                                                          ParameterExpression param,
                                                          ExpressionType      operation)
        {
            var expressions = rules.Select(r => GetExpressionForRule(type,
                                                                     r,
                                                                     param));

            return BinaryExpression(expressions,
                                    operation);
        }

        public Func<T, bool> CompileRule<T>(Rule r)
        {
            var paramUser = Expression.Parameter(typeof(T));
            var expr = GetExpressionForRule(typeof(T),
                                            r,
                                            paramUser);

            return Expression.Lambda<Func<T, bool>>(expr,
                                                    paramUser)
                             .Compile();
        }

        public Func<object, bool> CompileRule(Type type,
                                              Rule r)
        {
            var paramUser = Expression.Parameter(typeof(object));

            var expr = GetExpressionForRule(type,
                                            r,
                                            paramUser);

            return Expression.Lambda<Func<object, bool>>(expr,
                                                         paramUser)
                             .Compile();
        }

        public Func<T, bool> CompileRules<T>(IEnumerable<Rule> rules)
        {
            var paramUser = Expression.Parameter(typeof(T));

            var expr = BuildNestedExpression(typeof(T),
                                             rules,
                                             paramUser,
                                             ExpressionType.And);

            return Expression.Lambda<Func<T, bool>>(expr,
                                                    paramUser)
                             .Compile();
        }

        public Func<object, bool> CompileRules(Type              type,
                                               IEnumerable<Rule> rules)
        {
            var paramUser = Expression.Parameter(type);

            var expr = BuildNestedExpression(type,
                                             rules,
                                             paramUser,
                                             ExpressionType.And);

            return Expression.Lambda<Func<object, bool>>(expr,
                                                         paramUser)
                             .Compile();
        }

        private static Type ElementType(Type seqType)
        {
            var iEnum = FindIEnumerable(seqType);

            return iEnum == null ? seqType : iEnum.GetGenericArguments()[0];
        }

        private static Type FindIEnumerable(Type seqType)
        {
            if (seqType == null ||
                seqType == typeof(string))
            {
                return null;
            }

            if (seqType.IsArray)
            {
                return typeof(IEnumerable<>).MakeGenericType(seqType.GetElementType());
            }

            if (seqType.IsGenericType)
            {
                foreach (var arg in seqType.GetGenericArguments())
                {
                    var iEnum = typeof(IEnumerable<>).MakeGenericType(arg);

                    if (iEnum.IsAssignableFrom(seqType))
                    {
                        return iEnum;
                    }
                }
            }

            var iFaces = seqType.GetInterfaces();

            foreach (var iFace in iFaces)
            {
                var iEnum = FindIEnumerable(iFace);

                if (iEnum != null)
                {
                    return iEnum;
                }
            }

            if (seqType.BaseType != null &&
                seqType.BaseType != typeof(object))
            {
                return FindIEnumerable(seqType.BaseType);
            }

            return null;
        }

        private static Type GetCollectionItemType(Type collectionType)
        {
            if (collectionType.IsArray)
            {
                return collectionType.GetElementType();
            }

            return collectionType.GetInterface("IEnumerable") != null ? collectionType.GetGenericArguments()[0] : typeof(object);
        }

        private static Expression GetDataRowField(Expression prm,
                                                  string     member,
                                                  string     typeName)
        {
            var expMember = Expression.Call(prm,
                                            MiGetItem.Value,
                                            Expression.Constant(member,
                                                                typeof(string)));

            var type = Type.GetType(typeName);

            Debug.Assert(type != null);

            if (type.IsClass ||
                typeName.StartsWith("System.Nullable"))
            {
                //  equals "return  testValue == DBNull.Value  ? (typeName) null : (typeName) testValue"
                return Expression.Condition(Expression.Equal(expMember,
                                                             Expression.Constant(DBNull.Value)),
                                            Expression.Constant(null,
                                                                type),
                                            Expression.Convert(expMember,
                                                               type));
            }

            return Expression.Convert(expMember,
                                      type);
        }

        // Build() in some forks
        protected static Expression GetExpressionForRule(Type                type,
                                                         Rule                rule,
                                                         ParameterExpression parameterExpression)
        {
            ExpressionType nestedOperator;

            if (Enum.TryParse(rule.Operator,
                              out nestedOperator)        &&
                NestedOperators.Contains(nestedOperator) &&
                rule.Rules != null                       &&
                rule.Rules.Any())
            {
                return BuildNestedExpression(type,
                                             rule.Rules,
                                             parameterExpression,
                                             nestedOperator);
            }

            return BuildExpr(type,
                             rule,
                             parameterExpression);
        }

        private static MethodInfo GetLinqMethod(string name,
                                                int    numParameter)
        {
            return typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                                     .FirstOrDefault(m => m.Name == name &&
                                                          m.GetParameters()
                                                           .Length ==
                                                          numParameter);
        }

        private static Expression GetProperty(Expression param,
                                              string     propname)
        {
            var propExpression  = param;
            var childProperties = propname.Split('.');
            var propertyType    = param.Type;

            foreach (var childProp in childProperties)
            {
                var isIndexed = RegexIndexed.Match(childProp);

                if (isIndexed.Success)
                {
                    var collectionName = isIndexed.Groups[1]
                                                  .Value;

                    var index = int.Parse(isIndexed.Groups[2]
                                                   .Value);

                    var collExpr = GetProperty(param,
                                               collectionName);

                    var collectionType = collExpr.Type;

                    if (collectionType.IsArray)
                    {
                        propExpression = Expression.ArrayAccess(collExpr,
                                                                Expression.Constant(index));
                        propertyType = propExpression.Type;
                    }
                    else
                    {
                        var getter = collectionType.GetMethod("get_Item",
                                                              new[] {typeof(int)});
                        if (getter == null)
                        {
                            throw new RulesException($"'{collectionName} ({collectionType.Name}) cannot be indexed");
                        }

                        propExpression = Expression.Call(collExpr,
                                                         getter,
                                                         Expression.Constant(index));
                        propertyType = getter.ReturnType;
                    }
                }
                else
                {
                    var property = propertyType.GetProperty(childProp);

                    if (property == null)
                    {
                        throw new
                            RulesException($"Cannot find property {childProp} in class {propertyType.Name} (\"{propname}\")");
                    }

                    propExpression = Expression.PropertyOrField(propExpression,
                                                                childProp);
                    propertyType = property.PropertyType;
                }
            }

            return propExpression;
        }

        private static MethodInfo IsEnumerableOperator(string enumerableOperator)
        {
            return (from tup in EnumerableMethodsByName
                    where string.Equals(enumerableOperator,
                                        tup.Item1,
                                        StringComparison.CurrentCultureIgnoreCase)
                    select tup.Item2.Value)
                .FirstOrDefault();
        }

        public Expression<Func<T, bool>> RuleExpression<T>(Rule r)
        {
            var paramUser = Expression.Parameter(typeof(T));

            var expr = GetExpressionForRule(typeof(T),
                                            r,
                                            paramUser);

            return Expression.Lambda<Func<T, bool>>(expr,
                                                    paramUser);
        }

        public Expression<Func<object, bool>> RuleExpression(Type type,
                                                             Rule r)
        {
            var paramUser = Expression.Parameter(typeof(object));

            var expr = GetExpressionForRule(type,
                                            r,
                                            paramUser);

            return Expression.Lambda<Func<object, bool>>(expr,
                                                         paramUser);
        }

        public Expression<Func<T, bool>> RuleExpression<T>(IEnumerable<Rule> rules)
        {
            var paramUser = Expression.Parameter(typeof(T));

            var expr = BuildNestedExpression(typeof(T),
                                             rules,
                                             paramUser,
                                             ExpressionType.And);

            return Expression.Lambda<Func<T, bool>>(expr,
                                                    paramUser);
        }

        public Expression<Func<object, bool>> RuleExpression(Type              type,
                                                             IEnumerable<Rule> rules)
        {
            var paramUser = Expression.Parameter(type);
            var expr = BuildNestedExpression(type,
                                             rules,
                                             paramUser,
                                             ExpressionType.And);

            return Expression.Lambda<Func<object, bool>>(expr,
                                                         paramUser);
        }

        private static Expression StringToExpression(object value,
                                                     Type   propType)
        {
            Debug.Assert(propType != null);

            object safeValue;

            var valueType = propType;

            var txt = value as string;

            if (value == null)
            {
                safeValue = null;
            }
            else if (txt != null)
            {
                if (txt.ToLower() == "null")
                {
                    safeValue = null;
                }
                else if (propType.IsEnum)
                {
                    safeValue = Enum.Parse(propType,
                                           txt);
                }
                else
                {
                    safeValue = Convert.ChangeType(value,
                                                   valueType);
                }
            }
            else if (propType.Name == "Nullable`1")
            {
                valueType = Nullable.GetUnderlyingType(propType);
                safeValue = Convert.ChangeType(value,
                                               valueType);
            }
            else
            {
                safeValue = Convert.ChangeType(value,
                                               valueType);
            }

            return Expression.Constant(safeValue,
                                       propType);
        }
    }

    [DataContract]
    public class Rule
    {
        public Rule()
        {
            Inputs = Enumerable.Empty<object>();
        }

        [DataMember]
        public IEnumerable<object> Inputs { get; set; }

        [DataMember]
        public string MemberName { get; set; }

        [DataMember]
        public string Operator { get; set; }

        [DataMember]
        public IList<Rule> Rules { get; set; }

        [DataMember]
        public object TargetValue { get; set; }

        public static Rule All(string member,
                               Rule   rule)
        {
            return new Rule {MemberName = member, Operator = "All", Rules = new List<Rule> {rule}};
        }

        public static Rule Any(string member,
                               Rule   rule)
        {
            return new Rule {MemberName = member, Operator = "Any", Rules = new List<Rule> {rule}};
        }

        public static Rule Create(string      member,
                                  mreOperator oper,
                                  object      target)
        {
            return new Rule {MemberName = member, TargetValue = target, Operator = oper.ToString()};
        }

        public static Rule IsDecimal(string member)
        {
            return new Rule
                   {MemberName = member, Operator = "IsDecimal"};
        }

        public static Rule IsDouble(string member)
        {
            return new Rule
                   {MemberName = member, Operator = "IsDouble"};
        }

        public static Rule IsFloat(string member)
        {
            return new Rule
                   {MemberName = member, Operator = "IsSingle"};
        }

        public static Rule IsInteger(string member)
        {
            return new Rule
                   {MemberName = member, Operator = "IsInteger"};
        }

        public static Rule IsSingle(string member)
        {
            return new Rule
                   {MemberName = member, Operator = "IsSingle"};
        }

        private static Rule MergeRulesInto(Rule target,
                                           Rule lhs,
                                           Rule rhs)
        {
            target.Rules = new List<Rule>();

            if (lhs.Rules    != null &&
                lhs.Operator == target.Operator) // left is multiple
            {
                target.Rules.AddRange(lhs.Rules);
                if (rhs.Rules    != null &&
                    rhs.Operator == target.Operator)
                {
                    target.Rules.AddRange(rhs.Rules); // left & right are multiple
                }
                else
                {
                    target.Rules.Add(rhs); // left multi, right single
                }
            }
            else if (rhs.Rules    != null &&
                     rhs.Operator == target.Operator)
            {
                target.Rules.Add(lhs); // left single, right multi
                target.Rules.AddRange(rhs.Rules);
            }
            else
            {
                target.Rules.Add(lhs);
                target.Rules.Add(rhs);
            }


            return target;
        }

        public static Rule Method(string          methodName,
                                  params object[] inputs)
        {
            return new Rule {Inputs = inputs.ToList(), Operator = methodName};
        }

        public static Rule MethodOnChild(string          member,
                                         string          methodName,
                                         params object[] inputs)
        {
            return new Rule {MemberName = member, Inputs = inputs.ToList(), Operator = methodName};
        }

        public override string ToString()
        {
            if (TargetValue != null)
            {
                return $"{MemberName} {Operator} {TargetValue}";
            }

            if (Rules != null)
            {
                return Rules.Count == 1 ? $"{MemberName} {Operator} ({Rules[0]})" : $"{MemberName} {Operator} (Rules)";
            }

            return Inputs != null ? $"{MemberName} {Operator} (Inputs)" : $"{MemberName} {Operator}";
        }

        public static Rule operator &(Rule lhs,
                                      Rule rhs)
        {
            var rule = new Rule {Operator = "AndAlso"};
            return MergeRulesInto(rule,
                                  lhs,
                                  rhs);
        }

        public static Rule operator |(Rule lhs,
                                      Rule rhs)
        {
            var rule = new Rule {Operator = "Or"};
            return MergeRulesInto(rule,
                                  lhs,
                                  rhs);
        }
    }

    public class DataRule : Rule
    {
        public string Type { get; set; }

        public static DataRule Create<T>(string      member,
                                         mreOperator oper,
                                         T           target)
        {
            return new DataRule
                   {
                       MemberName  = member,
                       TargetValue = target,
                       Operator    = oper.ToString(),
                       Type        = typeof(T).FullName
                   };
        }

        public static DataRule Create<T>(string      member,
                                         mreOperator oper,
                                         string      target)
        {
            return new DataRule
                   {
                       MemberName  = member,
                       TargetValue = target,
                       Operator    = oper.ToString(),
                       Type        = typeof(T).FullName
                   };
        }

        public static DataRule Create(string      member,
                                      mreOperator oper,
                                      object      target,
                                      Type        memberType)
        {
            return new DataRule
                   {
                       MemberName  = member,
                       TargetValue = target,
                       Operator    = oper.ToString(),
                       Type        = memberType.FullName
                   };
        }
    }

    internal static class Placeholder
    {
        public static decimal Decimal;
        public static double  Double;
        public static float   Float;
        public static int     Int;
    }

    // Nothing specific to MRE.  Can be moved to a more common location
    public static class Extensions
    {
        public static void AddRange<T>(this IList<T>  collection,
                                       IEnumerable<T> newValues)
        {
            foreach (var item in newValues)
            {
                collection.Add(item);
            }
        }
    }

    public class RulesException : ApplicationException
    {
        public RulesException()
        {
        }

        public RulesException(string message) : base(message)
        {
        }

        public RulesException(string    message,
                              Exception innerException) : base(message,
                                                               innerException)
        {
        }
    }

    //
    // Summary:
    //     Describes the node types for the nodes of an expression tree.
    public enum mreOperator
    {
        //
        // Summary:
        //     An addition operation, such as a + b, without overflow checking, for numeric
        //     operands.
        Add = 0,

        //
        // Summary:
        //     A bitwise or logical AND operation, such as (a & b) in C# and (a And b) in Visual
        //     Basic.
        And = 2,

        //
        // Summary:
        //     A conditional AND operation that evaluates the second operand only if the first
        //     operand evaluates to true. It corresponds to (a && b) in C# and (a AndAlso b)
        //     in Visual Basic.
        AndAlso = 3,

        //
        // Summary:
        //     A node that represents an equality comparison, such as (a == b) in C# or (a =
        //     b) in Visual Basic.
        Equal = 13,

        //
        // Summary:
        //     A "greater than" comparison, such as (a > b).
        GreaterThan = 15,

        //
        // Summary:
        //     A "greater than or equal to" comparison, such as (a >= b).
        GreaterThanOrEqual = 16,

        //
        // Summary:
        //     A "less than" comparison, such as (a < b).
        LessThan = 20,

        //
        // Summary:
        //     A "less than or equal to" comparison, such as (a <= b).
        LessThanOrEqual = 21,

        //
        // Summary:
        //     An inequality comparison, such as (a != b) in C# or (a <> b) in Visual Basic.
        NotEqual = 35,

        //
        // Summary:
        //     A bitwise or logical OR operation, such as (a | b) in C# or (a Or b) in Visual
        //     Basic.
        Or = 36,

        //
        // Summary:
        //     A short-circuiting conditional OR operation, such as (a || b) in C# or (a OrElse
        //     b) in Visual Basic.
        OrElse = 37,

        IsMatch
    }

    public class RuleValue<T>
    {
        public List<Rule> Rules { get; set; }
        public T          Value { get; set; }
    }

    public class RuleValueString : RuleValue<string>
    {
    }
}
