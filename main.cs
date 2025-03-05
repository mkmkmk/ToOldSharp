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
            
            // Parsowanie kodu źródłowego
            SyntaxTree tree = CSharpSyntaxTree.ParseText(originalCode);
            var root = await tree.GetRootAsync() as CSharpSyntaxNode;
            
            if (root == null)
                return false;

            // Zastosowanie transformacji
            var newRoot = root;
            newRoot = (CSharpSyntaxNode)new DependencyPropertyRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new CollectionInitializerRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new ExpressionBodiedMemberRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new ObjectInitializerRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new NullForgivingRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new RecordRewriter().Visit(newRoot);
            
            // Zachowaj oryginalne formatowanie
            string newCode = newRoot.ToFullString();
            
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
    /// Komentuje inicjalizacje DependencyProperty, które mogą powodować problemy w Enterprise Architect
    /// </summary>
    class DependencyPropertyRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            // Sprawdź czy to jest deklaracja DependencyProperty
            if (node.Declaration.Type.ToString().Contains("DependencyProperty") &&
                node.Declaration.Variables.Any(v => v.Initializer != null))
            {
                // Tworzymy nową listę zmiennych, gdzie inicjalizatory są zakomentowane
                var newVariables = SyntaxFactory.SeparatedList<VariableDeclaratorSyntax>();
                
                foreach (var variable in node.Declaration.Variables)
                {
                    if (variable.Initializer != null)
                    {
                        // Utwórz komentarz z oryginalnym inicjalizatorem
                        var initializerText = variable.Initializer.ToString();
                        var commentText = $"/* {initializerText} */";
                        var commentTrivia = SyntaxFactory.SyntaxTrivia(SyntaxKind.MultiLineCommentTrivia, commentText);
                        
                        // Utwórz nowy deklarator bez inicjalizatora, ale z komentarzem
                        var newVariable = variable
                            .WithInitializer(null)
                            .WithTrailingTrivia(
                                variable.GetTrailingTrivia()
                                    .Add(commentTrivia)
                            );
                        
                        newVariables = newVariables.Add(newVariable);
                    }
                    else
                    {
                        newVariables = newVariables.Add(variable);
                    }
                }
                
                // Utwórz nową deklarację z zakomentowanymi inicjalizatorami
                var newDeclaration = node.Declaration.WithVariables(newVariables);
                return node.WithDeclaration(newDeclaration);
            }
            
            return base.VisitFieldDeclaration(node);
        }
    }

    /// <summary>
    /// Konwertuje inicjalizacje kolekcji z użyciem składni = []
    /// </summary>
    class CollectionInitializerRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.Initializer != null && node.Initializer.Value.ToString() == "[]")
            {
                // Znajdź typ właściwości
                string typeName = node.Type.ToString();
                
                // Utwórz nowy inicjalizator z pełną składnią
                string newInitText = $"new {typeName}()";
                var newInitializer = SyntaxFactory.EqualsValueClause(
                    SyntaxFactory.ParseExpression(newInitText)
                );
                
                return node.WithInitializer(newInitializer);
            }
            
            return base.VisitPropertyDeclaration(node);
        }
        
        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (node.Initializer != null && node.Initializer.Value.ToString() == "[]")
            {
                // Znajdź typ zmiennej
                var declaration = node.Parent as VariableDeclarationSyntax;
                if (declaration != null)
                {
                    string typeName = declaration.Type.ToString();
                    
                    // Utwórz nowy inicjalizator z pełną składnią
                    string newInitText = $"new {typeName}()";
                    var newInitializer = SyntaxFactory.EqualsValueClause(
                        SyntaxFactory.ParseExpression(newInitText)
                    );
                    
                    return node.WithInitializer(newInitializer);
                }
            }
            
            return base.VisitVariableDeclarator(node);
        }
    }
    
    /// <summary>
    /// Konwertuje wyrażenia lambda (expression-bodied members) na pełne metody z blokami
    /// </summary>
    class ExpressionBodiedMemberRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            // Jeśli właściwość ma ciało wyrażeniowe (=>), zamień na blok get z return
            if (node.ExpressionBody != null)
            {
                var returnStatement = SyntaxFactory.ReturnStatement(node.ExpressionBody.Expression)
                    // Dodaj spację po "return"
                    .WithReturnKeyword(
                        SyntaxFactory.Token(
                            SyntaxFactory.TriviaList(),
                            SyntaxKind.ReturnKeyword,
                            SyntaxFactory.TriviaList(SyntaxFactory.Space)
                        )
                    );
                    
                var getAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithBody(SyntaxFactory.Block(returnStatement))
                    // Dodaj spację po "get"
                    .WithKeyword(
                        SyntaxFactory.Token(
                            SyntaxFactory.TriviaList(),
                            SyntaxKind.GetKeyword,
                            SyntaxFactory.TriviaList(SyntaxFactory.Space)
                        )
                    );
                
                var accessorList = SyntaxFactory.AccessorList(
                    SyntaxFactory.SingletonList(getAccessor)
                )
                // Dodaj spacje wokół nawiasów klamrowych
                .WithOpenBraceToken(
                    SyntaxFactory.Token(
                        SyntaxFactory.TriviaList(SyntaxFactory.Space),
                        SyntaxKind.OpenBraceToken,
                        SyntaxFactory.TriviaList()
                    )
                )
                .WithCloseBraceToken(
                    SyntaxFactory.Token(
                        SyntaxFactory.TriviaList(),
                        SyntaxKind.CloseBraceToken,
                        SyntaxFactory.TriviaList()
                    )
                );
                
                return node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                    .WithAccessorList(accessorList);
            }
            
            return base.VisitPropertyDeclaration(node);
        }
        
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            // Jeśli metoda ma ciało wyrażeniowe (=>), zamień na blok kodu z return
            if (node.ExpressionBody != null)
            {
                var returnStatement = SyntaxFactory.ReturnStatement(node.ExpressionBody.Expression)
                    // Dodaj spację po "return"
                    .WithReturnKeyword(
                        SyntaxFactory.Token(
                            SyntaxFactory.TriviaList(),
                            SyntaxKind.ReturnKeyword,
                            SyntaxFactory.TriviaList(SyntaxFactory.Space)
                        )
                    );
                    
                var block = SyntaxFactory.Block(returnStatement)
                    // Dodaj spacje wokół nawiasów klamrowych
                    .WithOpenBraceToken(
                        SyntaxFactory.Token(
                            SyntaxFactory.TriviaList(SyntaxFactory.Space),
                            SyntaxKind.OpenBraceToken,
                            SyntaxFactory.TriviaList()
                        )
                    )
                    .WithCloseBraceToken(
                        SyntaxFactory.Token(
                            SyntaxFactory.TriviaList(),
                            SyntaxKind.CloseBraceToken,
                            SyntaxFactory.TriviaList()
                        )
                    );
                
                return node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                    .WithBody(block);
            }
            
            return base.VisitMethodDeclaration(node);
        }
    }
    
    /// <summary>
    /// Konwertuje inicjalizatory obiektów z użyciem składni = new()
    /// </summary>
    class ObjectInitializerRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (node.Initializer != null && 
                node.Initializer.Value is ObjectCreationExpressionSyntax objCreation &&
                objCreation.Type == null)
            {
                // Znajdź typ zmiennej
                var declaration = node.Parent as VariableDeclarationSyntax;
                if (declaration != null)
                {
                    string typeName = declaration.Type.ToString();
                    
                    // Utwórz nowy inicjalizator z pełną składnią
                    var newObjCreation = objCreation.WithType(
                        SyntaxFactory.ParseTypeName(typeName)
                    );
                    
                    return node.WithInitializer(
                        SyntaxFactory.EqualsValueClause(newObjCreation)
                    );
                }
            }
            
            return base.VisitVariableDeclarator(node);
        }
        
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.Initializer != null && 
                node.Initializer.Value is ObjectCreationExpressionSyntax objCreation &&
                objCreation.Type == null)
            {
                // Znajdź typ właściwości
                string typeName = node.Type.ToString();
                
                // Utwórz nowy inicjalizator z pełną składnią
                var newObjCreation = objCreation.WithType(
                    SyntaxFactory.ParseTypeName(typeName)
                );
                
                return node.WithInitializer(
                    SyntaxFactory.EqualsValueClause(newObjCreation)
                );
            }
            
            return base.VisitPropertyDeclaration(node);
        }
    }
    
    /// <summary>
    /// Usuwa operator wymuszania null (null!)
    /// </summary>
    class NullForgivingRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            if (node.Kind() == SyntaxKind.SuppressNullableWarningExpression)
            {
                // Zamień null! na po prostu null
                return node.Operand;
            }
            
            return base.VisitPostfixUnaryExpression(node);
        }
    }
    
    /// <summary>
    /// Konwertuje deklaracje record na zwykłe klasy
    /// </summary>
    class RecordRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            // Utwórz klasę z tymi samymi modyfikatorami i identyfikatorem
            var classDecl = SyntaxFactory.ClassDeclaration(node.Identifier)
                .WithModifiers(node.Modifiers);
            
            // Dodaj dziedziczenie, jeśli istnieje
            if (node.BaseList != null)
            {
                classDecl = classDecl.WithBaseList(node.BaseList);
            }
            
            // Jeśli to jest record z parametrami, utwórz odpowiednie właściwości
            if (node.ParameterList != null)
            {
                var properties = new List<MemberDeclarationSyntax>();
                
                foreach (var param in node.ParameterList.Parameters)
                {
                    // Utwórz właściwość dla każdego parametru
                    var property = SyntaxFactory.PropertyDeclaration(
                        param.Type,
                        param.Identifier
                    )
                    .WithModifiers(
                        SyntaxFactory.TokenList(
                            SyntaxFactory.Token(SyntaxKind.PublicKeyword)
                        )
                    )
                    .WithAccessorList(
                        SyntaxFactory.AccessorList(
                            SyntaxFactory.List(new[] {
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                            })
                        )
                    );
                    
                    properties.Add(property);
                }
                
                // Dodaj konstruktor, który inicjalizuje właściwości
                var constructorParams = SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(
                        node.ParameterList.Parameters
                    )
                );
                
                var constructorBody = SyntaxFactory.Block(
                    node.ParameterList.Parameters.Select(p => 
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(p.Identifier),
                                SyntaxFactory.IdentifierName(p.Identifier)
                            )
                        )
                    )
                );
                
                var constructor = SyntaxFactory.ConstructorDeclaration(node.Identifier)
                    .WithModifiers(
                        SyntaxFactory.TokenList(
                            SyntaxFactory.Token(SyntaxKind.PublicKeyword)
                        )
                    )
                    .WithParameterList(constructorParams)
                    .WithBody(constructorBody);
                
                properties.Add(constructor);
                
                // Dodaj członków do klasy
                classDecl = classDecl.WithMembers(
                    SyntaxFactory.List(properties)
                );
            }
            
            // Dodaj komentarz informujący o oryginalnej deklaracji record
            var recordComment = SyntaxFactory.Comment($"/* Original record declaration: {node.ToString().Trim()} */");
            classDecl = classDecl.WithLeadingTrivia(
                SyntaxFactory.TriviaList(
                    recordComment,
                    SyntaxFactory.CarriageReturnLineFeed
                )
            );
            
            return classDecl;
        }
    }
}
