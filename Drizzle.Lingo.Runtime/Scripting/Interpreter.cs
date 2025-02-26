﻿using System;
using System.Linq;
using System.Reflection;
using Drizzle.Lingo.Runtime.Parser;

namespace Drizzle.Lingo.Runtime.Scripting
{
    public class Interpreter
    {
        public static object? Evaluate(AstNode.Base expression, LingoRuntime runtime)
        {
            var scope = new InterpreterScope(runtime);

            return EvalNode(expression, scope);
        }

        private static object? EvalNode(AstNode.Base node, InterpreterScope scope)
        {
            return node switch
            {
                AstNode.Constant constant => EvalConstant(constant, scope),
                AstNode.Number number => number.Value,
                AstNode.GlobalCall globalCall => EvalGlobalCall(globalCall, scope),
                AstNode.List list => EvalList(list, scope),
                AstNode.PropertyList propertyList => EvalPropList(propertyList, scope),
                AstNode.String s => s.Value,
                AstNode.Symbol symbol => new LingoSymbol(symbol.Value),
                AstNode.UnaryOperator unaryOperator => EvalUnaryOp(unaryOperator, scope),
                _ => throw new ArgumentOutOfRangeException(nameof(node))
            };
        }

        private static object? EvalUnaryOp(AstNode.UnaryOperator unaryOperator, InterpreterScope scope)
        {
            dynamic? value = EvalNode(unaryOperator.Expression, scope);

            switch (unaryOperator.Type)
            {
                case AstNode.UnaryOperatorType.Negate:
                    return -value;
                case AstNode.UnaryOperatorType.Not:
                    return !value;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static object? EvalPropList(AstNode.PropertyList listNode, InterpreterScope scope)
        {
            var propList = new LingoPropertyList(listNode.Values.Length);

            foreach (var (key, value) in listNode.Values)
            {
                propList.Dict[EvalNode(key, scope)!] = EvalNode(value, scope);
            }

            return propList;
        }

        private static object? EvalList(AstNode.List listNode, InterpreterScope scope)
        {
            var list = new LingoList(listNode.Values.Length);

            foreach (var value in listNode.Values)
            {
                list.List.Add(EvalNode(value, scope));
            }

            return list;
        }

        private static object? EvalGlobalCall(AstNode.GlobalCall node, InterpreterScope scope)
        {
            var args = node.Arguments.Select(n => EvalNode(n, scope)).ToArray();

            // The interpret doesn't do `dynamic` dispatch. This can be problematic for some arg binding.
            // Just hardcoding the problem cases here.

            if (node.Name == "point")
            {
                var x = args[0];
                var y = args[1];

                if (x is LingoNumber xd && y is LingoNumber yd)
                    return new LingoPoint(xd, yd);

                if (x is int xi && y is int yi)
                    return new LingoPoint(xi, yi);

                throw new ArgumentException("Unknown point types!");
            }

            if (node.Name == "rect")
            {
                var x = args[0];
                var y = args[1];
                var z = args[2];
                var w = args[3];

                if (x is LingoNumber xd && y is LingoNumber yd && z is LingoNumber zd && w is LingoNumber wd)
                    return new LingoRect(xd, yd, zd, wd);

                if (x is int xi && y is int yi && z is int zi && w is int wi)
                    return new LingoRect(xi, yi, zi, wi);

                throw new ArgumentException("Unknown rect types!");
            }

            var members = typeof(LingoGlobal).GetMember(node.Name.ToLower())!;
            var method = members.OfType<MethodInfo>().First(m => m.GetParameters().Length == node.Arguments.Length);

            if (method.IsStatic)
                return method.Invoke(null, args);

            return method.Invoke(scope.Runtime.Global, args);
        }

        private static object? EvalConstant(AstNode.Constant constant, InterpreterScope scope)
        {
            return constant.Name switch
            {
                "backspace" => "\x08",
                "empty" => "",
                "enter" => "\x03",
                "false" => 0,
                "pi" => new LingoNumber(Math.PI),
                "quote" => "\"",
                "return" => "\r",
                "space" => " ",
                "true" => 1,
                "void" => null,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private sealed class InterpreterScope
        {
            public LingoRuntime Runtime { get; }

            public InterpreterScope(LingoRuntime runtime)
            {
                Runtime = runtime;
            }
        }
    }
}
