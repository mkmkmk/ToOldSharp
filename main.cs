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
            newRoot = (CSharpSyntaxNode)new StringInterpolationRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new NullConditionalRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new NullCoalescingRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new PatternMatchingRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new TupleRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new LocalFunctionRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new DependencyPropertyRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new CollectionInitializerRewriter().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new MethodCallInitializerRewriter().Visit(newRoot);
            
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
            // Jeśli metoda ma ciało wyrażeniowe (=>), zamień na blok kodu z return
            if (node.ExpressionBody != null)
            {
                var returnStatement = SyntaxFactory.ReturnStatement(node.ExpressionBody.Expression);
                var block = SyntaxFactory.Block(returnStatement);
                
                return node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                    .WithBody(block);
            }
            
            return base.VisitMethodDeclaration(node);
        }
        
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            // Jeśli właściwość ma ciało wyrażeniowe (=>), zamień na blok get z return
            if (node.ExpressionBody != null)
            {
                var returnStatement = SyntaxFactory.ReturnStatement(node.ExpressionBody.Expression);
                var getAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithBody(SyntaxFactory.Block(returnStatement));
                
                var accessorList = SyntaxFactory.AccessorList(
                    SyntaxFactory.SingletonList(getAccessor));
                
                return node
                    .WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
                    .WithAccessorList(accessorList);
            }
            
            return base.VisitPropertyDeclaration(node);
        }
    }

    /// <summary>
    /// Konwertuje inicjalizatory właściwości na tradycyjne deklaracje i inicjalizacje w konstruktorze
    /// </summary>
    class PropertyInitializerRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, List<(string PropertyName, ExpressionSyntax Initializer)>> _classInitializers = 
            new Dictionary<string, List<(string, ExpressionSyntax)>>();
        
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            // Jeśli właściwość ma inicjalizator, usuń go i zapamiętaj do dodania w konstruktorze
            if (node.Initializer != null)
            {
                // Znajdź klasę, w której znajduje się właściwość
                var classDeclaration = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                if (classDeclaration != null)
                {
                    string className = classDeclaration.Identifier.Text;
                    
                    if (!_classInitializers.ContainsKey(className))
                    {
                        _classInitializers[className] = new List<(string, ExpressionSyntax)>();
                    }
                    
                    _classInitializers[className].Add((node.Identifier.Text, node.Initializer.Value));
                    
                    return node
                        .WithInitializer(null)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                }
            }
            
            return base.VisitPropertyDeclaration(node);
        }
        
        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            // Najpierw przetwórz wszystkie właściwości w klasie
            var newNode = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
            
            // Sprawdź, czy mamy inicjalizatory do dodania
            string className = node.Identifier.Text;
            if (_classInitializers.TryGetValue(className, out var initializers) && initializers.Count > 0)
            {
                // Znajdź istniejący konstruktor lub utwórz nowy
                var constructor = newNode.Members
                    .OfType<ConstructorDeclarationSyntax>()
                    .FirstOrDefault(c => c.ParameterList.Parameters.Count == 0);
                
                if (constructor == null)
                {
                    // Utwórz nowy konstruktor
                    var statements = initializers.Select(i => 
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(i.PropertyName),
                                i.Initializer
                            )
                        )
                    );
                    
                    // Dodaj odpowiednie formatowanie
                    var leadingTrivia = SyntaxFactory.TriviaList(
                        SyntaxFactory.Whitespace("    ")  // Wcięcie 4 spacje
                    );
                    
                    var bodyLeadingTrivia = SyntaxFactory.TriviaList(
                        SyntaxFactory.Whitespace("    ")  // Wcięcie 4 spacje
                    );
                    
                    var statementsWithIndent = statements.Select(stmt => 
                        stmt.WithLeadingTrivia(
                            SyntaxFactory.TriviaList(
                                SyntaxFactory.Whitespace("        ")  // Wcięcie 8 spacji dla instrukcji
                            )
                        )
                    );
                    
                    constructor = SyntaxFactory.ConstructorDeclaration(className)
                        .WithModifiers(
                            SyntaxFactory.TokenList(
                                SyntaxFactory.Token(
                                    SyntaxFactory.TriviaList(),
                                    SyntaxKind.PublicKeyword,
                                    SyntaxFactory.TriviaList(SyntaxFactory.Space)
                                )
                            )
                        )
                        .WithParameterList(SyntaxFactory.ParameterList())
                        .WithBody(
                            SyntaxFactory.Block(statementsWithIndent)
                                .WithOpenBraceToken(
                                    SyntaxFactory.Token(
                                        SyntaxFactory.TriviaList(SyntaxFactory.Whitespace("    ")),
                                        SyntaxKind.OpenBraceToken,
                                        SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed)
                                    )
                                )
                                .WithCloseBraceToken(
                                    SyntaxFactory.Token(
                                        SyntaxFactory.TriviaList(SyntaxFactory.Whitespace("    ")),
                                        SyntaxKind.CloseBraceToken,
                                        SyntaxFactory.TriviaList()
                                    )
                                )
                        )
                        .WithLeadingTrivia(
                            SyntaxFactory.TriviaList(
                                SyntaxFactory.Whitespace("    "),
                                SyntaxFactory.CarriageReturnLineFeed
                            )
                        );
                    
                    return newNode.AddMembers(constructor);
                }
                else
                {
                    // Dodaj inicjalizatory do istniejącego konstruktora
                    var newStatements = initializers.Select(i => 
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(i.PropertyName),
                                i.Initializer
                            )
                        ).WithLeadingTrivia(
                            SyntaxFactory.TriviaList(
                                SyntaxFactory.Whitespace("        ")  // Wcięcie 8 spacji
                            )
                        )
                    );
                    
                    var newBody = constructor.Body.AddStatements(newStatements.ToArray());
                    var newConstructor = constructor.WithBody(newBody);
                    
                    return newNode.ReplaceNode(constructor, newConstructor);
                }
            }
            
            return newNode;
        }
    }


    /// <summary>
    /// Konwertuje interpolację stringów na string.Format
    /// </summary>
    class StringInterpolationRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            // Konwersja $"..." na string.Format("...", ...)
            var formatArgs = new List<ExpressionSyntax>();
            var formatStringParts = new List<string>();
            
            int index = 0;
            foreach (var content in node.Contents)
            {
                if (content is InterpolatedStringTextSyntax textPart)
                {
                    formatStringParts.Add(textPart.TextToken.ValueText);
                }
                else if (content is InterpolationSyntax interpolation)
                {
                    formatStringParts.Add("{" + index + (interpolation.FormatClause != null ? ":" + interpolation.FormatClause.ToString().Substring(1) : "") + "}");
                    formatArgs.Add(interpolation.Expression);
                    index++;
                }
            }
            
            var formatString = string.Join("", formatStringParts);
            var formatStringLiteral = SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(formatString)
            );
            
            var arguments = new List<ArgumentSyntax> { SyntaxFactory.Argument(formatStringLiteral) };
            arguments.AddRange(formatArgs.Select(arg => SyntaxFactory.Argument(arg)));
            
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("string"),
                    SyntaxFactory.IdentifierName("Format")
                ),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))
            );
        }
    }

    /// <summary>
    /// Konwertuje operatory warunkowe null (?.) na tradycyjne sprawdzanie null
    /// </summary>
    class NullConditionalRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            // Konwersja obj?.Property na (obj != null) ? obj.Property : null
            var condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.NotEqualsExpression,
                node.Expression,
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
            );
            
            // Obsługa różnych typów dostępu warunkowego
            ExpressionSyntax memberAccess;
            
            if (node.WhenNotNull is MemberBindingExpressionSyntax memberBinding)
            {
                // Przypadek obj?.Property
                memberAccess = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    node.Expression,
                    memberBinding.Name
                );
            }
            else if (node.WhenNotNull is InvocationExpressionSyntax invocation)
            {
                // Przypadek obj?.Method()
                if (invocation.Expression is MemberBindingExpressionSyntax methodBinding)
                {
                    var methodAccess = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        node.Expression,
                        methodBinding.Name
                    );
                    
                    memberAccess = SyntaxFactory.InvocationExpression(
                        methodAccess,
                        invocation.ArgumentList
                    );
                }
                else
                {
                    // Jeśli nie możemy obsłużyć tego przypadku, zwróć oryginalne wyrażenie
                    return base.VisitConditionalAccessExpression(node);
                }
            }
            else
            {
                // Jeśli nie możemy obsłużyć tego przypadku, zwróć oryginalne wyrażenie
                return base.VisitConditionalAccessExpression(node);
            }
            
            return SyntaxFactory.ConditionalExpression(
                condition,
                memberAccess,
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
            );
        }
    }

    /// <summary>
    /// Konwertuje operatory koalescencji null (??) na tradycyjne sprawdzanie null
    /// </summary>
    class NullCoalescingRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.Kind() == SyntaxKind.CoalesceExpression)
            {
                // Konwersja a ?? b na (a != null) ? a : b
                var condition = SyntaxFactory.BinaryExpression(
                    SyntaxKind.NotEqualsExpression,
                    node.Left,
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                );
                
                return SyntaxFactory.ConditionalExpression(
                    condition,
                    node.Left,
                    node.Right
                );
            }
            
            return base.VisitBinaryExpression(node);
        }
    }

    /// <summary>
    /// Konwertuje pattern matching na tradycyjne sprawdzanie typów
    /// </summary>
    class PatternMatchingRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            if (node.Pattern is DeclarationPatternSyntax declarationPattern)
            {
                // Konwersja "expr is Type var" na "expr is Type"
                var isExpression = SyntaxFactory.BinaryExpression(
                    SyntaxKind.IsExpression,
                    node.Expression,
                    declarationPattern.Type
                );
                
                // Znajdź if statement, który zawiera to wyrażenie
                var ifStatement = node.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
                if (ifStatement != null && ifStatement.Condition == node)
                {
                    // Dodaj deklarację zmiennej na początku bloku if
                    var variableName = declarationPattern.Designation is SingleVariableDesignationSyntax varDesignation
                        ? varDesignation.Identifier.Text
                        : "temp";
                    
                    var castedVariable = SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(
                            declarationPattern.Type,
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator(variableName)
                                    .WithInitializer(
                                        SyntaxFactory.EqualsValueClause(
                                            SyntaxFactory.CastExpression(
                                                declarationPattern.Type,
                                                node.Expression
                                            )
                                        )
                                    )
                            )
                        )
                    );
                    
                    // Dodaj deklarację zmiennej na początku bloku if
                    if (ifStatement.Statement is BlockSyntax block)
                    {
                        var newBlock = block.WithStatements(
                            SyntaxFactory.List(new[] { castedVariable }.Concat(block.Statements))
                        );
                        
                        // Zamiast zwracać nowy IfStatement, zapamiętaj go do późniejszej zamiany
                        var newIfStatement = SyntaxFactory.IfStatement(
                            isExpression,
                            newBlock,
                            ifStatement.Else
                        );
                        
                        // Zarejestruj zamianę do wykonania później
                        RegisterReplacement(ifStatement, newIfStatement);
                    }
                    else
                    {
                        var newBlock = SyntaxFactory.Block(
                            castedVariable,
                            ifStatement.Statement
                        );
                        
                        // Zamiast zwracać nowy IfStatement, zapamiętaj go do późniejszej zamiany
                        var newIfStatement = SyntaxFactory.IfStatement(
                            isExpression,
                            newBlock,
                            ifStatement.Else
                        );
                        
                        // Zarejestruj zamianę do wykonania później
                        RegisterReplacement(ifStatement, newIfStatement);
                    }
                }
                
                // Zawsze zwracaj wyrażenie, nie instrukcję
                return isExpression;
            }
            
            return base.VisitIsPatternExpression(node);
        }
        
        // Lista zamian do wykonania
        private readonly List<(SyntaxNode Original, SyntaxNode Replacement)> _replacements = 
            new List<(SyntaxNode, SyntaxNode)>();
        
        private void RegisterReplacement(SyntaxNode original, SyntaxNode replacement)
        {
            _replacements.Add((original, replacement));
        }
        
        public override SyntaxNode Visit(SyntaxNode node)
        {
            var result = base.Visit(node);
            
            // Wykonaj wszystkie zarejestrowane zamiany
            foreach (var (original, replacement) in _replacements)
            {
                if (result.Contains(original))
                {
                    result = result.ReplaceNode(original, replacement);
                }
            }
            
            return result;
        }
    }

    /// <summary>
    /// Konwertuje tuple na tradycyjne klasy Tuple lub ValueTuple
    /// </summary>
    class TupleRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitTupleExpression(TupleExpressionSyntax node)
        {
            // Konwersja (a, b) na new Tuple<object, object>(a, b)
            var arguments = SyntaxFactory.SeparatedList(
                node.Arguments.Select(arg => SyntaxFactory.Argument(arg.Expression))
            );
            
            // Utwórz typ generyczny Tuple<object, object, ...>
            var typeArgs = SyntaxFactory.SeparatedList<TypeSyntax>(
                Enumerable.Repeat(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)), node.Arguments.Count)
            );
            
            var tupleType = SyntaxFactory.GenericName(
                SyntaxFactory.Identifier("Tuple"),
                SyntaxFactory.TypeArgumentList(typeArgs)
            );
            
            return SyntaxFactory.ObjectCreationExpression(
                tupleType,
                SyntaxFactory.ArgumentList(arguments),
                null
            );
        }

        public override SyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (node.Left is TupleExpressionSyntax tupleExpr)
            {
                // Konwersja (a, b) = GetValues() na var temp = GetValues(); a = temp.Item1; b = temp.Item2;
                var tempVar = SyntaxFactory.IdentifierName("tupleTemp");
                var statements = new List<StatementSyntax>();
                
                // Deklaracja zmiennej tymczasowej
                statements.Add(
                    SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(
                            SyntaxFactory.IdentifierName("var"),
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.VariableDeclarator("tupleTemp")
                                    .WithInitializer(
                                        SyntaxFactory.EqualsValueClause(node.Right)
                                    )
                            )
                        )
                    )
                );
                
                // Przypisania do poszczególnych elementów tuple
                for (int i = 0; i < tupleExpr.Arguments.Count; i++)
                {
                    var itemAccess = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        tempVar,
                        SyntaxFactory.IdentifierName($"Item{i + 1}")
                    );
                    
                    statements.Add(
                        SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                ((ArgumentSyntax)tupleExpr.Arguments[i]).Expression,
                                itemAccess
                            )
                        )
                    );
                }
                
                // Zastąp wyrażenie przypisania blokiem kodu
                var block = SyntaxFactory.Block(statements);
                
                // Znajdź nadrzędną instrukcję i zastąp ją blokiem
                var parentStatement = node.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
                if (parentStatement != null)
                {
                    return block;
                }
            }
            
            return base.VisitAssignmentExpression(node);
        }
    }

    /// <summary>
    /// Konwertuje lokalne funkcje na prywatne metody klasy
    /// </summary>
    class LocalFunctionRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, List<LocalFunctionStatementSyntax>> _classLocalFunctions = 
            new Dictionary<string, List<LocalFunctionStatementSyntax>>();
        
        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            // Znajdź klasę, w której znajduje się lokalna funkcja
            var classDeclaration = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDeclaration != null)
            {
                string className = classDeclaration.Identifier.Text;
                
                if (!_classLocalFunctions.ContainsKey(className))
                {
                    _classLocalFunctions[className] = new List<LocalFunctionStatementSyntax>();
                }
                
                _classLocalFunctions[className].Add(node);
                
                // Usuń lokalną funkcję z oryginalnego miejsca
                return null;
            }
            
            return base.VisitLocalFunctionStatement(node);
        }
        
        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            // Najpierw przetwórz wszystkie elementy w klasie
            var newNode = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
            
            // Sprawdź, czy mamy lokalne funkcje do dodania jako metody
            string className = node.Identifier.Text;
            if (_classLocalFunctions.TryGetValue(className, out var localFunctions) && localFunctions.Count > 0)
            {
                // Konwertuj lokalne funkcje na prywatne metody
                var methods = localFunctions.Select(lf => 
                    SyntaxFactory.MethodDeclaration(
                        lf.ReturnType,
                        lf.Identifier
                    )
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
                    .WithParameterList(lf.ParameterList)
                    .WithBody(lf.Body)
                );
                
                return newNode.AddMembers(methods.ToArray());
            }
            
            return newNode;
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
        
        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            // Sprawdź czy to jest zmienna typu DependencyProperty
            var parentDeclaration = node.Parent as VariableDeclarationSyntax;
            if (parentDeclaration != null && 
                parentDeclaration.Type.ToString().Contains("DependencyProperty") &&
                node.Initializer != null)
            {
                // Utwórz komentarz z oryginalnym inicjalizatorem
                var initializerText = node.Initializer.ToString();
                var commentText = $"/* {initializerText} */";
                var commentTrivia = SyntaxFactory.SyntaxTrivia(SyntaxKind.MultiLineCommentTrivia, commentText);
                
                // Utwórz nowy deklarator bez inicjalizatora, ale z komentarzem
                return node
                    .WithInitializer(null)
                    .WithTrailingTrivia(
                        node.GetTrailingTrivia()
                            .Add(commentTrivia)
                    );
            }
            
            return base.VisitVariableDeclarator(node);
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
    /// Konwertuje inicjalizatory zawierające wywołania metod
    /// </summary>
    class MethodCallInitializerRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            // Sprawdź czy to jest pole z inicjalizatorem
            if (node.Declaration.Variables.Any(v => v.Initializer != null))
            {
                var newVariables = SyntaxFactory.SeparatedList<VariableDeclaratorSyntax>();
                bool modified = false;
                
                foreach (var variable in node.Declaration.Variables)
                {
                    if (variable.Initializer != null)
                    {
                        // Sprawdź czy inicjalizator zawiera wywołanie metody lub dostęp do członka
                        bool containsMethodCall = variable.Initializer.Value
                            .DescendantNodesAndSelf()
                            .OfType<InvocationExpressionSyntax>()
                            .Any();
                            
                        bool containsMemberAccess = variable.Initializer.Value
                            .DescendantNodesAndSelf()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Any(m => m.Name.Identifier.Text == "StartNew" || 
                                      m.Name.Identifier.Text.StartsWith("From"));
                        
                        // Sprawdź czy to nie jest DependencyProperty (już obsłużone przez DependencyPropertyRewriter)
                        bool isDependencyProperty = node.Declaration.Type.ToString().Contains("DependencyProperty");
                        
                        if ((containsMethodCall || containsMemberAccess) && !isDependencyProperty)
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
                            modified = true;
                        }
                        else
                        {
                            newVariables = newVariables.Add(variable);
                        }
                    }
                    else
                    {
                        newVariables = newVariables.Add(variable);
                    }
                }
                
                if (modified)
                {
                    // Utwórz nową deklarację z zakomentowanymi inicjalizatorami
                    var newDeclaration = node.Declaration.WithVariables(newVariables);
                    return node.WithDeclaration(newDeclaration);
                }
            }
            
            return base.VisitFieldDeclaration(node);
        }
        
        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (node.Initializer != null)
            {
                // Sprawdź czy inicjalizator zawiera wywołanie metody
                bool containsMethodCall = node.Initializer.Value
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any();
                    
                if (containsMethodCall)
                {
                    // Utwórz komentarz z oryginalnym inicjalizatorem
                    var initializerText = node.Initializer.ToString();
                    var commentText = $"/* {initializerText} */";
                    var commentTrivia = SyntaxFactory.SyntaxTrivia(SyntaxKind.MultiLineCommentTrivia, commentText);
                    
                    // Utwórz nowy deklarator bez inicjalizatora, ale z komentarzem
                    return node
                        .WithInitializer(null)
                        .WithTrailingTrivia(
                            node.GetTrailingTrivia()
                                .Add(commentTrivia)
                        );
                }
            }
            
            return base.VisitVariableDeclarator(node);
        }
        
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.Initializer != null)
            {
                // Sprawdź czy inicjalizator zawiera wywołanie metody
                bool containsMethodCall = node.Initializer.Value
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any();
                    
                if (containsMethodCall)
                {
                    // Utwórz komentarz z oryginalnym inicjalizatorem
                    var initializerText = node.Initializer.ToString();
                    var commentText = $"/* {initializerText} */";
                    var commentTrivia = SyntaxFactory.SyntaxTrivia(SyntaxKind.MultiLineCommentTrivia, commentText);
                    
                    // Utwórz nowy deklarator bez inicjalizatora, ale z komentarzem
                    return node
                        .WithInitializer(null)
                        .WithTrailingTrivia(
                            node.GetTrailingTrivia()
                                .Add(commentTrivia)
                        );
                }
            }
            
            return base.VisitPropertyDeclaration(node);
        }
        
    }
}


