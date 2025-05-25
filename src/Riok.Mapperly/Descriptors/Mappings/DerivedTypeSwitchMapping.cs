using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Riok.Mapperly.Emit.Syntax;
using Riok.Mapperly.Helpers;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Riok.Mapperly.Emit.Syntax.SyntaxFactoryHelper;

namespace Riok.Mapperly.Descriptors.Mappings;

/// <summary>
/// A derived type mapping maps one base type or interface to another
/// by implementing a type switch over known types and performs the provided mapping for each type.
/// </summary>
public class DerivedTypeSwitchMapping(ITypeSymbol sourceType, ITypeSymbol targetType, IReadOnlyCollection<INewInstanceMapping> typeMappings)
    : NewInstanceMethodMapping(sourceType, targetType)
{
    private const string GetTypeMethodName = nameof(GetType);

    public override IEnumerable<StatementSyntax> BuildBody(TypeMappingBuildContext ctx)
    {
        // static ADto Throw(A source) => throw new ArgumentException(msg, nameof(ctx.Source));
        var sourceTypeExpr = ctx.SyntaxFactory.Invocation(MemberAccess(ctx.Source, GetTypeMethodName));
        var throwHelper = LocalFunctionStatement(FullyQualifiedIdentifier(TargetType).AddTrailingSpace(), Identifier("Throw"))
            .WithLeadingTrivia()
            .WithModifiers(TokenList(TrailingSpacedToken(SyntaxKind.StaticKeyword)))
            .WithParameterList(
                ParameterList(
                    Token(SyntaxKind.OpenParenToken),
                    SingletonSeparatedList(Parameter(SourceType.FullyQualifiedIdentifierName(), ctx.Source.ToString())),
                    Token(SyntaxKind.CloseParenToken)
                )
            )
            .WithExpressionBody(
                ArrowExpressionClause(
                        ThrowArgumentExpression(
                                InterpolatedString(
                                    $"Cannot map {sourceTypeExpr} to {TargetType.ToDisplayString()} as there is no known derived type mapping"
                                ),
                                ctx.Source
                            )
                            .AddLeadingSpace()
                    )
                    .AddLeadingSpace()
            )
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        // _ => Throw(source)
        var fallbackArm = SwitchArm(DiscardPattern(), ctx.SyntaxFactory.Invocation("Throw", ctx.Source));

        // source switch { A x => MapToADto(x), B x => MapToBDto(x) }
        var (typeArmContext, typeArmVariableName) = ctx.WithNewSource();
        var arms = typeMappings.Select(x => BuildSwitchArm(typeArmVariableName, x.SourceType, x.Build(typeArmContext))).Append(fallbackArm);
        var switchExpression = ctx.SyntaxFactory.Switch(ctx.Source, arms);
        return
        [
            ctx.SyntaxFactory.Return(switchExpression).AddTrailingLineFeed(0),
            throwHelper.AddLeadingLineFeed(ctx.SyntaxFactory.Indentation),
        ];
    }

    private SwitchExpressionArmSyntax BuildSwitchArm(string typeArmVariableName, ITypeSymbol type, ExpressionSyntax mapping)
    {
        // A x => MapToADto(x),
        var declaration = DeclarationPattern(
            FullyQualifiedIdentifier(type.NonNullable()).AddTrailingSpace(),
            SingleVariableDesignation(Identifier(typeArmVariableName))
        );
        return SwitchArm(declaration, mapping);
    }
}
