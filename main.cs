using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
            newRoot = (CSharpSyntaxNode)new NullOperatorsRemover().Visit(newRoot);
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

}

