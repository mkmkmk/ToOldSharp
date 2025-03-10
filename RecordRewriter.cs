using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
/// <summary>
/// Konwertuje rekordy na klasy
/// </summary>
class RecordRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        // Pobierz parametry konstruktora z deklaracji rekordu
        var parameterList = node.ParameterList;

        if (parameterList == null || !parameterList.Parameters.Any())
        {
            // Jeśli rekord nie ma parametrów, zamień go na prostą klasę
            var classDeclaration = SyntaxFactory.ClassDeclaration(node.Identifier)
                .WithModifiers(node.Modifiers)
                .WithBaseList(node.BaseList)
                .WithTypeParameterList(node.TypeParameterList)
                .WithMembers(node.Members)
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());

            return classDeclaration;
        }

        // Utwórz klasę z tymi samymi modyfikatorami, identyfikatorem i listą bazową
        var newClassDeclaration = SyntaxFactory.ClassDeclaration(node.Identifier)
            .WithModifiers(node.Modifiers)
            .WithBaseList(node.BaseList)
            .WithTypeParameterList(node.TypeParameterList)
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(node.GetTrailingTrivia());

        // Utwórz właściwości na podstawie parametrów rekordu
        var properties = new List<MemberDeclarationSyntax>();
        var constructorParameters = new List<ParameterSyntax>();
        var constructorAssignments = new List<StatementSyntax>();

        foreach (var parameter in parameterList.Parameters)
        {
            // Utwórz właściwość
            var propertyName = char.ToUpper(parameter.Identifier.Text[0]) + parameter.Identifier.Text.Substring(1);
            var property = SyntaxFactory.PropertyDeclaration(parameter.Type, propertyName)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.List(new[] {
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
                                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                        })
                    )
                );

            properties.Add(property);

            // Dodaj parametr do konstruktora
            constructorParameters.Add(parameter);

            // Dodaj przypisanie w konstruktorze
            var assignment = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(propertyName),
                    SyntaxFactory.IdentifierName(parameter.Identifier.Text)
                )
            );

            constructorAssignments.Add(assignment);
        }

        // Utwórz konstruktor
        var constructor = SyntaxFactory.ConstructorDeclaration(node.Identifier.Text)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(constructorParameters)))
            .WithBody(SyntaxFactory.Block(constructorAssignments));

        // Dodaj konstruktor i właściwości do klasy
        var members = new List<MemberDeclarationSyntax>();
        members.AddRange(properties);
        members.Add(constructor);
        members.AddRange(node.Members);

        newClassDeclaration = newClassDeclaration.WithMembers(SyntaxFactory.List(members));

        return newClassDeclaration.NormalizeWhitespace();
    }
}


