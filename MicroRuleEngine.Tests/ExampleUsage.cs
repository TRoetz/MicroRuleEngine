using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MicroRuleEngine.Tests.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MicroRuleEngine.Tests
{
    /// <summary>
    ///     Summary description for UnitTest1
    /// </summary>
    [TestClass]
    public class ExampleUsage
    {
        [TestMethod]
        public void AnyOperator()
        {
            var order = GetOrder();

            //order.Items.Any(a => a.ItemCode == "test")
            var rule = new Rule
                       {
                           MemberName = "Items", // The array property
                           Operator   = "Any",
                           Rules = new[]
                                   {
                                       new Rule
                                       {
                                           MemberName  = "ItemCode", // the property in the above array item
                                           Operator    = "Equal",
                                           TargetValue = "Test"
                                       }
                                   }
                       };
            var engine     = new MRE();
            var boolMethod = engine.CompileRule<Order>(rule);
            var passes     = boolMethod(order);
            Assert.IsTrue(passes);

            var item = order.Items.First(x => x.ItemCode == "Test");
            item.ItemCode = "Changed";
            passes        = boolMethod(order);
            Assert.IsFalse(passes);
        }

        [TestMethod]
        public void BooleanMethods()
        {
            var order = GetOrder();
            var rule = new Rule
                       {
                           Operator = "HasItem", //The Order Object Contains a method named 'HasItem' that returns true/false
                           Inputs   = new List<object> {"Test"}
                       };
            var engine = new MRE();

            var boolMethod = engine.CompileRule<Order>(rule);
            var passes     = boolMethod(order);
            Assert.IsTrue(passes);

            var item = order.Items.First(x => x.ItemCode == "Test");
            item.ItemCode = "Changed";
            passes        = boolMethod(order);
            Assert.IsFalse(passes);
        }

        [TestMethod]
        public void BooleanMethods_ByType()
        {
            var order = GetOrder();
            var rule = new Rule
                       {
                           Operator = "HasItem", //The Order Object Contains a method named 'HasItem' that returns true/false
                           Inputs   = new List<object> {"Test"}
                       };
            var engine = new MRE();

            var boolMethod = engine.CompileRule(typeof(Order),
                                                rule);
            var passes = (bool) boolMethod.DynamicInvoke(order);
            Assert.IsTrue(passes);

            var item = order.Items.First(x => x.ItemCode == "Test");
            item.ItemCode = "Changed";
            passes        = (bool) boolMethod.DynamicInvoke(order);
            Assert.IsFalse(passes);
        }

        [TestMethod]
        public void ChildProperties()
        {
            var order = GetOrder();
            var rule = new Rule
                       {
                           MemberName  = "Customer.Country.CountryCode",
                           Operator    = ExpressionType.Equal.ToString("g"),
                           TargetValue = "AUS"
                       };
            var engine       = new MRE();
            var compiledRule = engine.CompileRule<Order>(rule);
            var passes       = compiledRule(order);
            Assert.IsTrue(passes);

            order.Customer.Country.CountryCode = "USA";
            passes                             = compiledRule(order);
            Assert.IsFalse(passes);
        }

        /// <summary>
        ///     With out error handling this throws an exception.
        ///     With error handling EF usage fails
        /// </summary>
        [TestMethod]
        public void ChildPropertiesOfNull()
        {
            var order = GetOrder();
            order.Customer = null;
            var rule = new Rule
                       {
                           MemberName  = "Customer.Country.CountryCode",
                           Operator    = ExpressionType.Equal.ToString("g"),
                           TargetValue = "AUS"
                       };
            var engine       = new MRE();
            var compiledRule = engine.CompileRule<Order>(rule);
            var passes       = compiledRule(order);
            Assert.IsFalse(passes);
        }

        [TestMethod]
        public void ChildPropertyBooleanMethods()
        {
            var order = GetOrder();
            var rule = new Rule
                       {
                           MemberName = "Customer.FirstName",
                           Operator =
                               "EndsWith", //Regular method that exists on string.. As a note expression methods are not available
                           Inputs = new List<object> {"ohn"}
                       };
            var engine         = new MRE();
            var childPropCheck = engine.CompileRule<Order>(rule);
            var passes         = childPropCheck(order);
            Assert.IsTrue(passes);

            order.Customer.FirstName = "jane";
            passes                   = childPropCheck(order);
            Assert.IsFalse(passes);
        }

        /// <summary>
        ///     With out error handling this throws an exception.
        ///     With error handling EF usage fails
        /// </summary>
        [TestMethod]
        public void ChildPropertyOfNullBooleanMethods()
        {
            var order = GetOrder();
            order.Customer = null;
            var rule = new Rule
                       {
                           MemberName = "Customer.FirstName",
                           Operator =
                               "EndsWith", //Regular method that exists on string.. As a note expression methods are not available
                           Inputs = new List<object> {"ohn"}
                       };
            var engine         = new MRE();
            var childPropCheck = engine.CompileRule<Order>(rule);
            var passes         = childPropCheck(order);
            Assert.IsFalse(passes);
        }

        [TestMethod]
        public void ConditionalLogic()
        {
            var order = GetOrder();
            var rule = new Rule
                       {
                           Operator = ExpressionType.AndAlso.ToString("g"),
                           Rules = new List<Rule>
                                   {
                                       new Rule {MemberName = "Customer.LastName", TargetValue = "Doe", Operator = "Equal"},
                                       new Rule
                                       {
                                           Operator = "Or",
                                           Rules = new List<Rule>
                                                   {
                                                       new Rule
                                                       {
                                                           MemberName = "Customer.FirstName", TargetValue = "John",
                                                           Operator   = "Equal"
                                                       },
                                                       new Rule
                                                       {
                                                           MemberName = "Customer.FirstName", TargetValue = "Jane",
                                                           Operator   = "Equal"
                                                       }
                                                   }
                                       }
                                   }
                       };
            var engine   = new MRE();
            var fakeName = engine.CompileRule<Order>(rule);
            var passes   = fakeName(order);
            Assert.IsTrue(passes);

            order.Customer.FirstName = "Philip";
            passes                   = fakeName(order);
            Assert.IsFalse(passes);
        }

        public static Order GetOrder()
        {
            var order = new Order
                        {
                            OrderId = 1,
                            Customer = new Customer
                                       {
                                           FirstName = "John",
                                           LastName  = "Doe",
                                           Country = new Country
                                                     {
                                                         CountryCode = "AUS"
                                                     }
                                       },
                            Items = new List<Item>
                                    {
                                        new Item {ItemCode = "MM23", Cost = 5.25M},
                                        new Item {ItemCode = "LD45", Cost = 5.25M},
                                        new Item {ItemCode = "Test", Cost = 3.33M}
                                    },
                            Total = 13.83m,
                            OrderDate = new DateTime(1776,
                                                     7,
                                                     4),
                            Status = Status.Open
                        };
            return order;
        }

        [TestMethod]
        public void RegexIsMatch() //Had to add a Regex evaluator to make it feel 'Complete'
        {
            var order = GetOrder();
            var rule = new Rule
                       {
                           MemberName  = "Customer.FirstName",
                           Operator    = "IsMatch",
                           TargetValue = @"^[a-zA-Z0-9]*$"
                       };
            var engine     = new MRE();
            var regexCheck = engine.CompileRule<Order>(rule);
            var passes     = regexCheck(order);
            Assert.IsTrue(passes);

            order.Customer.FirstName = "--NoName";
            passes                   = regexCheck(order);
            Assert.IsFalse(passes);
        }
    }
}
