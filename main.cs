using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Editing;

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
            string originalCode = File.ReadAllText(filePath);
            
            // Parsowanie kodu źródłowego z zachowaniem trywialnych elementów (komentarze, białe znaki)
            SyntaxTree tree = CSharpSyntaxTree.ParseText(originalCode, new CSharpParseOptions(
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
            
            // Zachowaj oryginalne formatowanie - nie używaj Formatter.Format
            string newCode = newRoot.ToFullString();
            
            // Dodaj w metodzie ProcessFileAsync przed zapisem pliku
            if (newCode != originalCode)
            {
                // Utwórz kopię zapasową
                File.WriteAllText(filePath + ".bak", originalCode);
                
                // Zapisz zmodyfikowany plik
                File.WriteAllText(filePath, newCode);
                Console.WriteLine($"Zmodyfikowano: {filePath}");
                return true;
            }            
                        
            return false;
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
                var comment = SyntaxFactory.Comment($" // => {originalExpression}");
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
                
                // Dodaj komentarz z oryginalnym wyrażeniem
                var comment = SyntaxFactory.Comment($" //=> {originalExpression}");
                var trivia = SyntaxFactory.TriviaList(comment);
                
                // Utwórz nową deklarację właściwości
                var newNode = node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                    .WithAccessorList(accessorList)
                    .WithTrailingTrivia(trivia);
                
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
                        // Zamień akcesory z ciałami wyrażeniowymi na zwykłe akcesory
                        var newAccessor = accessor
                            .WithExpressionBody(null)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                            .WithBody(null);
                        
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
                var comment = SyntaxFactory.Comment($" // = {initializerValue}");
                var trivia = node.GetTrailingTrivia().Add(comment);
                
                // Utwórz nową deklarację właściwości bez inicjalizatora
                var newNode = node
                    .WithInitializer(null)
                    .WithSemicolonToken(node.SemicolonToken)
                    .WithTrailingTrivia(trivia);
                
                return newNode;
            }
            
            return base.VisitPropertyDeclaration(node);
        }
        
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            // Sprawdź, czy któraś z deklaracji zmiennych ma inicjalizator
            var variables = node.Declaration.Variables;
            var modifiedVariables = new List<VariableDeclaratorSyntax>();
            bool modified = false;
            
            foreach (var variable in variables)
            {
                if (variable.Initializer != null)
                {
                    // Pobierz oryginalne wyrażenie inicjalizatora
                    var initializerValue = variable.Initializer.Value.ToString();
                    
                    // Utwórz nową deklarację zmiennej bez inicjalizatora
                    var newVariable = variable
                        .WithInitializer(null);
                    
                    modifiedVariables.Add(newVariable);
                    modified = true;
                    
                    // Dodaj komentarz z oryginalnym inicjalizatorem
                    node = node.WithTrailingTrivia(
                        node.GetTrailingTrivia().Add(
                            SyntaxFactory.Comment($" //= {initializerValue}")
                        )
                    );
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
                
                return node.WithDeclaration(newDeclaration);
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
            
            return newClassDeclaration;
        }
    }
}
