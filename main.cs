using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpLegacyConverter
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Konwerter nowych konstrukcji C# na starsze wersje (Roslyn)");
            Console.WriteLine("----------------------------------------------------------");

            string directoryPath;
            if (args.Length > 0)
            {
                directoryPath = args[0];
            }
            else
            {
                Console.Write("Podaj ścieżkę do katalogu z plikami C#: ");
                directoryPath = Console.ReadLine()?.Trim('"') ?? "";
            }

            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine($"Katalog {directoryPath} nie istnieje!");
                return;
            }

            // await ProcessFileAsync(directoryPath + "/AvalonDock.4.72.1/source/AvalonDocPanelMemoryLeaks/MainWindow.xaml.cs");
            // await ProcessFileAsync(directoryPath + "/AvalonDock.4.72.1/source/AutomationTest/AvalonDockTest/TestHelpers/SwitchContextToUiThreadAwaiter.cs");
            // await ProcessFileAsync(directoryPath + "/SymuLBNP_2_4/Controls/Led.xaml.cs");
            // return;

            try
            {
                await ProcessDirectoryAsync(directoryPath);
                Console.WriteLine("Konwersja zakończona pomyślnie.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Wystąpił błąd: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static async Task ProcessDirectoryAsync(string directoryPath)
        {
            int processedFiles = 0;
            int modifiedFiles = 0;

            // Pobierz wszystkie pliki C# w katalogu i podkatalogach
            var files = Directory.GetFiles(directoryPath, "*.cs", SearchOption.AllDirectories);

            Console.WriteLine($"Znaleziono {files.Length} plików C# do przetworzenia.");

            foreach (var file in files)
            {
                bool fileModified = await ProcessFileAsync(file);
                processedFiles++;

                if (fileModified)
                    modifiedFiles++;

                if (processedFiles % 10 == 0)
                    Console.WriteLine($"Przetworzono {processedFiles} plików...");
            }

            Console.WriteLine($"Przetworzono {processedFiles} plików, zmodyfikowano {modifiedFiles} plików.");
        }

        static async Task<bool> ProcessFileAsync(string filePath)
        {
            if (filePath.EndsWith(".g.cs"))
                return false;

            string originalCode = File.ReadAllText(filePath);
            var modNamespaceCode = new NamespaceConverter().Convert(originalCode);
            // var modNamespaceCode = originalCode;

            // Parsowanie kodu źródłowego z zachowaniem trywialnych elementów (komentarze, białe znaki)
            SyntaxTree tree = CSharpSyntaxTree.ParseText(modNamespaceCode, new CSharpParseOptions(
                preprocessorSymbols: null,
                documentationMode: DocumentationMode.Parse,
                kind: SourceCodeKind.Regular,
                languageVersion: LanguageVersion.Latest
            ));

            var root = await tree.GetRootAsync() as CSharpSyntaxNode;

            if (root == null)
                return false;

            // Zastosowanie transformacji z zachowaniem formatowania
            var newRoot = root;
            newRoot = (CSharpSyntaxNode)new ExpressionBodiedMemberRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new PropertyInitializerRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new InitOnlyPropertyRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new RecordRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new NullForgivingOperatorRemover().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new GlobalUsingCommenter().Visit(newRoot);

            // Zachowaj oryginalne formatowanie - nie używaj Formatter.Format
            string newCode = newRoot.ToFullString();

            // Dodaj w metodzie ProcessFileAsync przed zapisem pliku
            if (newCode != originalCode)
            {
                // Utwórz kopię zapasową
                // File.WriteAllText(filePath + ".bak", originalCode);

                // Zapisz zmodyfikowany plik
                File.WriteAllText(filePath, newCode);
                Console.WriteLine($"Zmodyfikowano: {filePath}");
                return true;
            }

            return false;
        }
    }


    /// <summary>
    /// Konwerter Roslyn usuwający operator ! z wartości domyślnych parametrów funkcji, pól i zdarzeń
    /// </summary>
    public class NullForgivingOperatorRemover : CSharpSyntaxRewriter
    {
        // Metoda pomocnicza do usuwania operatora ! z wyrażenia
        private ExpressionSyntax RemoveNullForgivingOperator(ExpressionSyntax expression)
        {
            if (expression is PostfixUnaryExpressionSyntax postfixExpression &&
                postfixExpression.OperatorToken.IsKind(SyntaxKind.ExclamationToken))
            {
                return postfixExpression.Operand;
            }
            return expression;
        }

        // Obsługa parametrów funkcji
        public override SyntaxNode VisitParameter(ParameterSyntax node)
        {
            if (node.Default != null)
            {
                var newValue = RemoveNullForgivingOperator(node.Default.Value);
                if (newValue != node.Default.Value)
                {
                    return node.WithDefault(
                        SyntaxFactory.EqualsValueClause(newValue)
                            .WithLeadingTrivia(node.Default.GetLeadingTrivia())
                            .WithTrailingTrivia(node.Default.GetTrailingTrivia())
                    );
                }
            }
            return base.VisitParameter(node);
        }

        // Obsługa pól
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            return VisitMemberDeclaration(node);
        }

        // Obsługa zdarzeń
        public override SyntaxNode VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
        {
            return VisitMemberDeclaration(node);
        }

        // Wspólna metoda dla pól i zdarzeń
        private SyntaxNode VisitMemberDeclaration<T>(T node) where T : MemberDeclarationSyntax
        {
            var declaration = node is FieldDeclarationSyntax field ? field.Declaration :
                            (node is EventFieldDeclarationSyntax eventField ? eventField.Declaration : null);

            if (declaration != null)
            {
                bool changed = false;
                var newVariables = new SeparatedSyntaxList<VariableDeclaratorSyntax>();

                foreach (var variable in declaration.Variables)
                {
                    var newVariable = variable;
                    if (variable.Initializer != null)
                    {
                        var newValue = RemoveNullForgivingOperator(variable.Initializer.Value);
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
                    var newDeclaration = declaration.WithVariables(newVariables);

                    if (node is FieldDeclarationSyntax fieldDecl)
                        return fieldDecl.WithDeclaration((VariableDeclarationSyntax)newDeclaration);
                    else if (node is EventFieldDeclarationSyntax eventDecl)
                        return eventDecl.WithDeclaration((VariableDeclarationSyntax)newDeclaration);
                }
            }

            return node;
        }

        // Nie odwiedzamy ciał metod - nadpisujemy te metody, aby zatrzymać przechodzenie w dół drzewa
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            // Przetwarzamy tylko parametry metody
            var newParams = (ParameterListSyntax)Visit(node.ParameterList);
            if (newParams != node.ParameterList)
            {
                return node.WithParameterList(newParams);
            }
            return node;
        }

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

        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            // Przetwarzamy tylko parametry funkcji lokalnej
            var newParams = (ParameterListSyntax)Visit(node.ParameterList);
            if (newParams != node.ParameterList)
            {
                return node.WithParameterList(newParams);
            }
            return node;
        }
    }


    public class NamespaceConverter
    {
        public string Convert(string sourceCode)
        {
            try
            {
                // Parsuj kod źródłowy
                SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
                CompilationUnitSyntax root = (CompilationUnitSyntax)tree.GetRoot();

                // Znajdź deklarację namespace z składnią z średnikiem
                var fileScopedNamespace = root.Members.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
                if (fileScopedNamespace == null)
                {
                    return sourceCode; // Nic do zmiany
                }

                // Pobierz tekst deklaracji namespace (bez średnika)
                string namespaceDeclaration = fileScopedNamespace.ToString().TrimEnd(';', ' ', '\r', '\n', '\t');

                // Znajdź indeks średnika w oryginalnym kodzie
                int semicolonIndex = fileScopedNamespace.SemicolonToken.SpanStart;

                // Podziel kod na części
                string beforeSemicolon = sourceCode.Substring(0, semicolonIndex);
                string afterSemicolon = sourceCode.Substring(semicolonIndex + 1); // +1 aby pominąć średnik

                // Utwórz nowy kod z deklaracją namespace z klamrami
                return beforeSemicolon + "\n{\n" + afterSemicolon + "\n}";
            }
            catch (Exception ex)
            {
                // W przypadku błędu, zwróć oryginalny kod
                Console.WriteLine($"Błąd podczas konwersji: {ex.Message}");
                return sourceCode;
            }
        }
    }



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


    /// <summary>
    /// Konwertuje właściwości init-only na zwykłe właściwości
    /// </summary>
    class InitOnlyPropertyRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.AccessorList != null)
            {
                var accessors = node.AccessorList.Accessors;
                var modifiedAccessors = new List<AccessorDeclarationSyntax>();
                bool modified = false;

                foreach (var accessor in accessors)
                {
                    if (accessor.Keyword.IsKind(SyntaxKind.InitKeyword))
                    {
                        // Zamień init na set
                        var newAccessor = accessor
                            .WithKeyword(SyntaxFactory.Token(SyntaxKind.SetKeyword));

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

}
