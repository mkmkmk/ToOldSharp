using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
/// <summary>
/// Konwerter Roslyn zamieniający "global using" na zakomentowane dyrektywy
/// </summary>
public class GlobalUsingCommenter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
    {
        // Sprawdzamy, czy to jest dyrektywa "global using"
        if (node.GlobalKeyword != default && !node.GlobalKeyword.IsKind(SyntaxKind.None))
        {
            // Tworzymy komentarz z oryginalnym tekstem
            var leadingTrivia = SyntaxFactory.TriviaList(
                SyntaxFactory.Comment("/* global */ "));

            // Tworzymy nową dyrektywę using bez słowa kluczowego "global"
            return node
                .WithGlobalKeyword(SyntaxFactory.Token(SyntaxKind.None))
                .WithLeadingTrivia(leadingTrivia.AddRange(node.GetLeadingTrivia()));
        }

        // Dla zwykłych dyrektyw using nie wprowadzamy zmian
        return base.VisitUsingDirective(node);
    }
}


