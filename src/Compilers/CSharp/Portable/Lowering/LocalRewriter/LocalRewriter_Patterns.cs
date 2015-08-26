﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;
using System;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed partial class LocalRewriter
    {
        public override BoundNode VisitIsPattern(BoundIsPattern node)
        {
            var expression = VisitExpression(node.Expression);
            var result = TranslatePattern(expression, node.Pattern);
            return result;
        }

        // input must be used no more than once in the result.
        BoundExpression TranslatePattern(BoundExpression input, BoundPattern pattern)
        {
            var syntax = _factory.Syntax = pattern.Syntax;
            switch(pattern.Kind)
            {
                case BoundKind.DeclarationPattern:
                    {
                        var declPattern = (BoundDeclarationPattern)pattern;
                        var type = declPattern.DeclaredType.Type;
                        // a pattern match of the form "expression is Type identifier" is equivalent to
                        // an invocation of one of these helpers:
                        if (type.IsReferenceType)
                        {
                            // bool Is<T>(object e, out T t) where T : class // reference type
                            // {
                            //     t = e as T;
                            //     return t != null;
                            // }
                            var assignment = _factory.AssignmentExpression(_factory.Local(declPattern.LocalSymbol), _factory.As(input, type));
                            var result = _factory.ObjectNotEqual(_factory.Local(declPattern.LocalSymbol), _factory.Null(type));
                            return _factory.Sequence(assignment, result);
                        }
                        else if (type.IsValueType)
                        {
                            // bool Is<T>(object e, out T t) where T : struct // non-Nullable value type
                            // {
                            //     T? tmp = e as T?;
                            //     t = tmp.GetValueOrDefault();
                            //     return tmp != null;
                            // }
                            var tmpType = _factory.SpecialType(SpecialType.System_Nullable_T).Construct(type);
                            var tmp = _factory.SynthesizedLocal(tmpType, syntax);
                            var asg1 = _factory.AssignmentExpression(_factory.Local(tmp), _factory.As(input, tmpType));
                            var asg2 = _factory.Call(_factory.Local(tmp), GetNullableMethod(syntax, tmpType, SpecialMember.System_Nullable_T_GetValueOrDefault));
                            var result = MakeNullableHasValue(syntax, _factory.Local(tmp));
                            return _factory.Sequence(tmp, asg1, asg2, result);
                        }
                        else
                        {
                            // if Type is a generic type parameter that is not constrained to any of these cases,
                            // then it was an error in semantic analysis. In that case we should not be here.
                            throw new NotImplementedException();
                        }
                    }
                default:
                    throw ExceptionUtilities.UnexpectedValue(pattern.Kind);
            }
        }
    }
}
