using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
/// <summary>
/// Konwerter Roslyn usuwający operatory ! oraz ? z typów i wartości domyślnych parametrów funkcji, pól i zdarzeń
/// </summary>
public class NullOperatorsRemover : CSharpSyntaxRewriter
{
    // Flaga określająca, czy wartość jest zbyt skomplikowana do automatycznej konwersji
    private bool IsComplexExpression(ExpressionSyntax expression)
    {
        // Sprawdzamy, czy wyrażenie zawiera operatory warunkowe, wywołania metod, itp.
        return expression.DescendantNodes().Any(n =>
            n is ConditionalExpressionSyntax || // ternary operator (a ? b : c)
            n is InvocationExpressionSyntax ||  // wywołania metod
            n is LambdaExpressionSyntax ||      // wyrażenia lambda
            n is QueryExpressionSyntax);        // wyrażenia LINQ
    }

    // Metoda pomocnicza do usuwania operatorów ! i ? z wyrażenia
    private ExpressionSyntax ProcessInitializerExpression(ExpressionSyntax expression)
    {
        // Jeśli wyrażenie jest zbyt skomplikowane, dodajemy komentarz i zwracamy oryginalne wyrażenie
        if (IsComplexExpression(expression))
        {
            // Dodajemy komentarz przed wyrażeniem
            var commentTrivia = SyntaxFactory.TriviaList(
                SyntaxFactory.Comment("/* UWAGA: Skomplikowane wyrażenie z operatorami nullowalnymi */"),
                SyntaxFactory.CarriageReturnLineFeed);

            return expression.WithLeadingTrivia(
                commentTrivia.AddRange(expression.GetLeadingTrivia()));
        }

        // Usuwanie operatora !
        if (expression is PostfixUnaryExpressionSyntax postfixExpression &&
            postfixExpression.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
        {
            return ProcessInitializerExpression(postfixExpression.Operand);
        }

        // Usuwanie operatora ?? (null coalescing)
        if (expression is BinaryExpressionSyntax binaryExpression &&
            binaryExpression.OperatorToken.IsKind(SyntaxKind.QuestionQuestionToken))
        {
            // Zamieniamy a ?? b na a
            return ProcessInitializerExpression(binaryExpression.Left);
        }

        // Usuwanie operatora ?. (conditional access) - tylko w prostych przypadkach
        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            // Zamieniamy obj?.Property na obj.Property
            var memberBinding = conditionalAccess.WhenNotNull as MemberBindingExpressionSyntax;
            if (memberBinding != null)
            {
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ProcessInitializerExpression(conditionalAccess.Expression),
                    memberBinding.Name);
            }
        }

        return expression;
    }

    // Metoda do bezpiecznego usuwania operatora ? z typu z zachowaniem trivia
    private TypeSyntax RemoveNullableOperator(NullableTypeSyntax nullableType)
    {
        // Zachowujemy wszystkie trivia z oryginalnego typu
        var elementType = nullableType.ElementType;

        // Pobieramy trivia z operatora ? i dodajemy je do elementType
        var questionMarkTrivia = nullableType.QuestionToken.TrailingTrivia;

        // Zwracamy typ elementu z zachowaniem wszystkich trivia
        return elementType.WithTrailingTrivia(
            elementType.GetTrailingTrivia().AddRange(questionMarkTrivia)
        );
    }

    // Rekurencyjne przetwarzanie typów, w tym typów zagnieżdżonych w typach generycznych
    private TypeSyntax ProcessType(TypeSyntax type)
    {
        if (type is NullableTypeSyntax nullableType)
        {
            return RemoveNullableOperator(nullableType);
        }
        else if (type is GenericNameSyntax genericName)
        {
            // Sprawdzamy, czy którykolwiek z argumentów typu jest nullowalny
            bool hasNullableTypeArgument = genericName.TypeArgumentList.Arguments.Any(arg =>
                arg is NullableTypeSyntax ||
                (arg is GenericNameSyntax nestedGeneric &&
                 nestedGeneric.TypeArgumentList.Arguments.Any(nestedArg => nestedArg is NullableTypeSyntax)));

            if (hasNullableTypeArgument)
            {
                // Tworzymy nową listę argumentów typu
                var newArguments = new SeparatedSyntaxList<TypeSyntax>();

                foreach (var arg in genericName.TypeArgumentList.Arguments)
                {
                    // Rekurencyjnie przetwarzamy każdy argument typu
                    newArguments = newArguments.Add(ProcessType(arg));
                }

                // Tworzymy nową listę argumentów typu
                var newTypeArgumentList = SyntaxFactory.TypeArgumentList(newArguments);

                // Zwracamy zmodyfikowany węzeł
                return genericName.WithTypeArgumentList(newTypeArgumentList);
            }
        }
        else if (type is ArrayTypeSyntax arrayType)
        {
            // Obsługa tablic z typami nullowalnymi, np. byte[]?
            var elementType = arrayType.ElementType;
            if (elementType is NullableTypeSyntax nullableElementType)
            {
                return SyntaxFactory.ArrayType(
                    RemoveNullableOperator(nullableElementType),
                    arrayType.RankSpecifiers);
            }
        }
        else if (type is QualifiedNameSyntax qualifiedName)
        {
            // Obsługa kwalifikowanych nazw typów, np. System.Collections.Generic.List<string?>
            var right = qualifiedName.Right;
            if (right is GenericNameSyntax genericRight)
            {
                var processedRight = ProcessType(genericRight);
                if (processedRight != right)
                {
                    return qualifiedName.WithRight((SimpleNameSyntax)processedRight);
                }
            }
        }

        return type;
    }

    // Obsługa parametrów funkcji
    public override SyntaxNode VisitParameter(ParameterSyntax node)
    {
        // Zachowujemy oryginalny węzeł do porównania
        var originalNode = node;

        // Obsługa typów nullowalnych w parametrach, w tym typów generycznych
        if (node.Type != null)
        {
            var newType = ProcessType(node.Type);
            if (newType != node.Type)
            {
                // Zachowujemy wszystkie trivia, w tym spacje między typem a nazwą
                var typeTrailingTrivia = node.Type.GetTrailingTrivia();
                var identifierLeadingTrivia = node.Identifier.LeadingTrivia;

                // Tworzymy nowy typ z zachowaniem trivia
                newType = newType.WithTrailingTrivia(typeTrailingTrivia);

                // Tworzymy nowy węzeł parametru
                node = node.WithType(newType);

                // Upewniamy się, że zachowujemy spację między typem a nazwą
                node = node.WithIdentifier(
                    node.Identifier.WithLeadingTrivia(identifierLeadingTrivia)
                );
            }
        }

        // Obsługa wartości domyślnych
        if (node.Default != null)
        {
            var newValue = ProcessInitializerExpression(node.Default.Value);
            if (newValue != node.Default.Value)
            {
                node = node.WithDefault(
                    SyntaxFactory.EqualsValueClause(newValue)
                        .WithLeadingTrivia(node.Default.GetLeadingTrivia())
                        .WithTrailingTrivia(node.Default.GetTrailingTrivia())
                );
            }
        }

        // Jeśli nic się nie zmieniło, zwracamy oryginalny węzeł
        if (node == originalNode)
        {
            return base.VisitParameter(node);
        }

        return node;
    }

    // Obsługa pól
    public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        // Obsługa typów nullowalnych w polach, w tym typów generycznych
        var newNode = node;
        var newType = ProcessType(node.Declaration.Type);
        if (newType != node.Declaration.Type)
        {
            // Zachowujemy wszystkie trivia, w tym spacje między typem a nazwą zmiennej
            var typeTrailingTrivia = node.Declaration.Type.GetTrailingTrivia();
            newType = newType.WithTrailingTrivia(typeTrailingTrivia);

            newNode = newNode.WithDeclaration(
                newNode.Declaration.WithType(newType)
            );
        }

        // Przetwarzanie inicjalizatorów pól
        bool changed = false;
        var newVariables = new SeparatedSyntaxList<VariableDeclaratorSyntax>();

        foreach (var variable in newNode.Declaration.Variables)
        {
            var newVariable = variable;
            if (variable.Initializer != null)
            {
                var newValue = ProcessInitializerExpression(variable.Initializer.Value);
                if (newValue != variable.Initializer.Value)
                {
                    newVariable = variable.WithInitializer(
                        SyntaxFactory.EqualsValueClause(newValue)
                            .WithLeadingTrivia(variable.Initializer.GetLeadingTrivia())
                            .WithTrailingTrivia(variable.Initializer.GetTrailingTrivia())
                    );
                    changed = true;
                }
            }
            newVariables = newVariables.Add(newVariable);
        }

        if (changed)
        {
            newNode = newNode.WithDeclaration(
                newNode.Declaration.WithVariables(newVariables)
            );
        }

        return newNode;
    }

    // Obsługa zdarzeń
    public override SyntaxNode VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        // Obsługa typów nullowalnych w zdarzeniach, w tym typów generycznych
        var newNode = node;
        var newType = ProcessType(node.Declaration.Type);
        if (newType != node.Declaration.Type)
        {
            // Zachowujemy wszystkie trivia, w tym spacje między typem a nazwą zdarzenia
            var typeTrailingTrivia = node.Declaration.Type.GetTrailingTrivia();
            newType = newType.WithTrailingTrivia(typeTrailingTrivia);

            newNode = newNode.WithDeclaration(
                newNode.Declaration.WithType(newType)
            );
        }

        // Przetwarzanie inicjalizatorów zdarzeń
        bool changed = false;
        var newVariables = new SeparatedSyntaxList<VariableDeclaratorSyntax>();

        foreach (var variable in newNode.Declaration.Variables)
        {
            var newVariable = variable;
            if (variable.Initializer != null)
            {
                var newValue = ProcessInitializerExpression(variable.Initializer.Value);
                if (newValue != variable.Initializer.Value)
                {
                    newVariable = variable.WithInitializer(
                        SyntaxFactory.EqualsValueClause(newValue)
                            .WithLeadingTrivia(variable.Initializer.GetLeadingTrivia())
                            .WithTrailingTrivia(variable.Initializer.GetTrailingTrivia())
                    );
                    changed = true;
                }
            }
            newVariables = newVariables.Add(newVariable);
        }

        if (changed)
        {
            newNode = newNode.WithDeclaration(
                newNode.Declaration.WithVariables(newVariables)
            );
        }

        return newNode;
    }

    // Obsługa właściwości
    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var newNode = node;

        // Obsługa typów nullowalnych w właściwościach, w tym typów generycznych
        var newType = ProcessType(node.Type);
        if (newType != node.Type)
        {
            // Zachowujemy wszystkie trivia, w tym spacje między typem a nazwą właściwości
            var typeTrailingTrivia = node.Type.GetTrailingTrivia();
            newType = newType.WithTrailingTrivia(typeTrailingTrivia);

            newNode = newNode.WithType(newType);
        }

        // Przetwarzamy tylko inicjalizator właściwości, nie ciało
        if (newNode.Initializer != null)
        {
            var newValue = ProcessInitializerExpression(newNode.Initializer.Value);
            if (newValue != newNode.Initializer.Value)
            {
                newNode = newNode.WithInitializer(
                    SyntaxFactory.EqualsValueClause(newValue)
                        .WithLeadingTrivia(newNode.Initializer.GetLeadingTrivia())
                        .WithTrailingTrivia(newNode.Initializer.GetTrailingTrivia())
                );
            }
        }

        // Nie przetwarzamy ciała właściwości (gettery/settery)
        return newNode;
    }

    // Obsługa metod - tylko typy i parametry, nie ciała
    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var newNode = node;

        // Obsługa typów nullowalnych w zwracanych typach metod, w tym typów generycznych
        var newReturnType = ProcessType(node.ReturnType);
        if (newReturnType != node.ReturnType)
        {
            // Zachowujemy wszystkie trivia, w tym spacje między typem zwracanym a nazwą metody
            var typeTrailingTrivia = node.ReturnType.GetTrailingTrivia();
            newReturnType = newReturnType.WithTrailingTrivia(typeTrailingTrivia);

            newNode = newNode.WithReturnType(newReturnType);
        }

        // Przetwarzamy tylko parametry metody
        var newParams = (ParameterListSyntax)Visit(node.ParameterList);
        if (newParams != node.ParameterList)
        {
            newNode = newNode.WithParameterList(newParams);
        }

        // Nie przetwarzamy ciała metody
        return newNode;
    }

    // Obsługa konstruktorów - tylko parametry, nie ciała
    public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        // Przetwarzamy tylko parametry konstruktora
        var newParams = (ParameterListSyntax)Visit(node.ParameterList);
        if (newParams != node.ParameterList)
        {
            return node.WithParameterList(newParams);
        }
        return node;
    }

    // Obsługa funkcji lokalnych - tylko typy i parametry, nie ciała
    public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        var newNode = node;

        // Obsługa typów nullowalnych w zwracanych typach funkcji lokalnych, w tym typów generycznych
        var newReturnType = ProcessType(node.ReturnType);
        if (newReturnType != node.ReturnType)
        {
            // Zachowujemy wszystkie trivia, w tym spacje między typem zwracanym a nazwą funkcji
            var typeTrailingTrivia = node.ReturnType.GetTrailingTrivia();
            newReturnType = newReturnType.WithTrailingTrivia(typeTrailingTrivia);

            newNode = newNode.WithReturnType(newReturnType);
        }

        // Przetwarzamy tylko parametry funkcji lokalnej
        var newParams = (ParameterListSyntax)Visit(node.ParameterList);
        if (newParams != node.ParameterList)
        {
            newNode = newNode.WithParameterList(newParams);
        }

        // Nie przetwarzamy ciała funkcji lokalnej
        return newNode;
    }

    // Obsługa delegatów
    public override SyntaxNode VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        var newNode = node;

        // Obsługa typów nullowalnych w zwracanych typach delegatów, w tym typów generycznych
        var newReturnType = ProcessType(node.ReturnType);
        if (newReturnType != node.ReturnType)
        {
            // Zachowujemy wszystkie trivia, w tym spacje między typem zwracanym a nazwą delegata
            var typeTrailingTrivia = node.ReturnType.GetTrailingTrivia();
            newReturnType = newReturnType.WithTrailingTrivia(typeTrailingTrivia);

            newNode = newNode.WithReturnType(newReturnType);
        }

        // Przetwarzamy parametry delegata
        var newParams = (ParameterListSyntax)Visit(node.ParameterList);
        if (newParams != node.ParameterList)
        {
            newNode = newNode.WithParameterList(newParams);
        }

        return newNode;
    }

    // Obsługa deklaracji zmiennych lokalnych
    public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        // Obsługa typów nullowalnych w zmiennych lokalnych, w tym typów generycznych
        var newNode = node;
        var newType = ProcessType(node.Declaration.Type);
        if (newType != node.Declaration.Type)
        {
            // Zachowujemy wszystkie trivia, w tym spacje między typem a nazwą zmiennej
            var typeTrailingTrivia = node.Declaration.Type.GetTrailingTrivia();
            newType = newType.WithTrailingTrivia(typeTrailingTrivia);

            newNode = newNode.WithDeclaration(
                newNode.Declaration.WithType(newType)
            );
        }

        return newNode;
    }

    // Obsługa deklaracji używania (using)
    public override SyntaxNode VisitUsingStatement(UsingStatementSyntax node)
    {
        // Obsługa typów nullowalnych w deklaracjach using, w tym typów generycznych
        if (node.Declaration != null)
        {
            var newType = ProcessType(node.Declaration.Type);
            if (newType != node.Declaration.Type)
            {
                // Zachowujemy wszystkie trivia, w tym spacje między typem a nazwą zmiennej
                var typeTrailingTrivia = node.Declaration.Type.GetTrailingTrivia();
                newType = newType.WithTrailingTrivia(typeTrailingTrivia);

                return node.WithDeclaration(
                    node.Declaration.WithType(newType)
                );
            }
        }

        return node;
    }

    // Obsługa wyrażeń typu (typeof, is, as)
    public override SyntaxNode VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        var newType = ProcessType(node.Type);
        if (newType != node.Type)
        {
            return node.WithType(newType);
        }
        return node;
    }

    // Obsługa wyrażeń is
    public override SyntaxNode VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        // Nie przetwarzamy wzorców w wyrażeniach is
        return node;
    }

    // Obsługa wyrażeń as
    public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        // Obsługa wyrażeń as z typami nullowalnymi
        if (node.OperatorToken.IsKind(SyntaxKind.AsKeyword) && node.Right is TypeSyntax typeNode)
        {
            var newType = ProcessType(typeNode);
            if (newType != typeNode)
            {
                return node.WithRight(newType);
            }
        }

        // Nie przetwarzamy innych wyrażeń binarnych
        return node;
    }
}


