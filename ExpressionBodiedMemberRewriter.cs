using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
/// <summary>
/// Konwertuje wyrażenia lambda (expression-bodied members) na pełne metody z blokami
/// </summary>
class ExpressionBodiedMemberRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        // Jeśli metoda ma ciało wyrażeniowe (=>)
        if (node.ExpressionBody != null)
        {
            // Pobierz oryginalne wyrażenie
            var originalExpression = node.ExpressionBody.Expression.ToString();

            // Utwórz nowe ciało metody jako pusty blok
            var newBody = SyntaxFactory.Block();

            // Dodaj komentarz z oryginalnym wyrażeniem
            var comment = SyntaxFactory.Comment($" /* => {originalExpression} */");
            var trivia = SyntaxFactory.TriviaList(comment);

            // Utwórz nową deklarację metody z blokiem zamiast wyrażenia
            var newNode = node
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                .WithBody(newBody)
                .WithTrailingTrivia(trivia);

            return newNode;
        }

        return base.VisitMethodDeclaration(node);
    }


    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        // Jeśli właściwość ma ciało wyrażeniowe (=>)
        if (node.ExpressionBody != null)
        {
            // Pobierz oryginalne wyrażenie
            var originalExpression = node.ExpressionBody.Expression.ToString();

            // Utwórz nową właściwość z getterem i setterem
            var accessorList = SyntaxFactory.AccessorList(
                SyntaxFactory.List(new[] {
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                })
            );

            // Zbierz wszystkie trivia z expression body i średnika
            var expressionBodyTrivia = node.ExpressionBody.GetLeadingTrivia()
                .AddRange(node.ExpressionBody.GetTrailingTrivia());

            var semicolonTrivia = node.SemicolonToken.LeadingTrivia
                .AddRange(node.SemicolonToken.TrailingTrivia);

            // Utwórz komentarz z oryginalnym wyrażeniem
            var comment = SyntaxFactory.Comment($" /* => {originalExpression} */");

            // Połącz wszystkie trivia
            var allTrivia = SyntaxFactory.TriviaList(comment)
                .AddRange(expressionBodyTrivia)
                .AddRange(semicolonTrivia);

            // Dodaj trivia do zamykającego nawiasu klamrowego
            accessorList = accessorList.WithCloseBraceToken(
                accessorList.CloseBraceToken.WithTrailingTrivia(allTrivia)
            );

            // Utwórz nową deklarację właściwości
            var newNode = node
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                .WithAccessorList(accessorList);
            // .WithTrailingTrivia(trivia);

            return newNode;
        }

        // Jeśli właściwość ma akcesory z ciałami wyrażeniowymi
        if (node.AccessorList != null)
        {
            var accessors = node.AccessorList.Accessors;
            var modifiedAccessors = new List<AccessorDeclarationSyntax>();
            bool modified = false;

            foreach (var accessor in accessors)
            {
                if (accessor.ExpressionBody != null)
                {
                    // Pobierz oryginalne wyrażenie
                    var originalExpression = accessor.ExpressionBody.Expression.ToString();

                    // Utwórz nowy akcesor bez ciała wyrażeniowego
                    var newAccessor = accessor
                        .WithExpressionBody(null)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                        .WithBody(null);

                    // Dodaj komentarz z oryginalnym wyrażeniem
                    var comment = SyntaxFactory.Comment($" /* => {originalExpression} */");
                    newAccessor = newAccessor.WithTrailingTrivia(
                        newAccessor.GetTrailingTrivia().Add(comment)
                    );

                    modifiedAccessors.Add(newAccessor);
                    modified = true;
                }
                else
                {
                    modifiedAccessors.Add(accessor);
                }
            }

            if (modified)
            {
                var newAccessorList = SyntaxFactory.AccessorList(
                    SyntaxFactory.List(modifiedAccessors)
                );

                return node.WithAccessorList(newAccessorList);
            }
        }

        return base.VisitPropertyDeclaration(node);
    }
}


