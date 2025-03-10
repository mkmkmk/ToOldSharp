using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
/// <summary>
/// Konwertuje inicjalizatory właściwości na tradycyjne deklaracje
/// </summary>
class PropertyInitializerRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        // Jeśli właściwość ma inicjalizator
        if (node.Initializer != null)
        {
            // Pobierz oryginalne wyrażenie inicjalizatora
            var initializerValue = node.Initializer.Value.ToString();

            // Dodaj komentarz z oryginalnym inicjalizatorem
            var comment = SyntaxFactory.Comment($" /* = {initializerValue} */");
            var newTrivia = SyntaxTriviaList.Create(comment).AddRange(node.GetTrailingTrivia());

            if (node.AccessorList != null)
            {
                // Utwórz nową deklarację właściwości bez inicjalizatora i bez średnika
                // Ważne: nie dodajemy średnika, ponieważ już jest w AccessorList
                var newNode = node
                .WithInitializer(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                //.WithTrailingTrivia(comment);
                .WithAccessorList(
                    node.AccessorList.WithCloseBraceToken(
                        node.AccessorList.CloseBraceToken.WithTrailingTrivia(newTrivia)
                    )
                );
                return newNode;
            }
            else
            {
                throw new NotImplementedException("mariusz jednak weź ten kod poniżej !!!!!!!!!");
                // Dla właściwości z expression body (=>)
                // var newNode = node
                //     .WithInitializer(null)
                //     .WithSemicolonToken(
                //         SyntaxFactory.Token(
                //             SyntaxTriviaList.Empty,
                //             SyntaxKind.SemicolonToken,
                //             ";",  // tekst tokenu
                //             ";",  // wartość tokenu
                //             newTrivia
                //         )

                //     );
                // return newNode;

            }
        }

        return base.VisitPropertyDeclaration(node);
    }

    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        // // Sprawdź, czy pole jest publiczne
        // bool isPublic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));

        // // Jeśli pole nie jest publiczne, nie modyfikuj go
        // if (!isPublic)
        //     return base.VisitFieldDeclaration(node);

        // Sprawdź, czy któraś z deklaracji zmiennych ma inicjalizator
        var variables = node.Declaration.Variables;
        var modifiedVariables = new List<VariableDeclaratorSyntax>();
        bool modified = false;
        string initializerComment = "";

        foreach (var variable in variables)
        {
            if (variable.Initializer != null)
            {
                // Pobierz oryginalne wyrażenie inicjalizatora
                var initializerValue = variable.Initializer.Value.ToString();
                initializerComment = $"/* = {initializerValue} */";

                // Utwórz nową deklarację zmiennej bez inicjalizatora
                var newVariable = variable
                    .WithInitializer(null);

                modifiedVariables.Add(newVariable);
                modified = true;
            }
            else
            {
                modifiedVariables.Add(variable);
            }
        }

        if (modified)
        {
            var newDeclaration = node.Declaration.WithVariables(
                SyntaxFactory.SeparatedList(modifiedVariables)
            );

            // Znajdź token średnika
            var semicolonToken = node.SemicolonToken;

            // Pobierz istniejące trailing trivia średnika
            var existingTrivia = semicolonToken.TrailingTrivia;

            // Utwórz nowe trivia, dodając komentarz przed istniejącymi trivia
            var newTrivia = SyntaxTriviaList.Create(SyntaxFactory.SyntaxTrivia(SyntaxKind.WhitespaceTrivia, " "))
                .Add(SyntaxFactory.SyntaxTrivia(SyntaxKind.MultiLineCommentTrivia, initializerComment));

            // Dodaj istniejące trivia na końcu
            foreach (var trivia in existingTrivia)
            {
                newTrivia = newTrivia.Add(trivia);
            }

            // Utwórz nowy token średnika z nowymi trivia
            var newSemicolonToken = semicolonToken.WithTrailingTrivia(newTrivia);

            return node
                .WithDeclaration(newDeclaration)
                .WithSemicolonToken(newSemicolonToken);
        }

        return base.VisitFieldDeclaration(node);
    }

}


