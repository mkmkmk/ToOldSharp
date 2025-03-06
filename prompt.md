proszę napisz mi program konsolowy dla .net8 który w zadanym katalogu przerabia pliki C# z nowych konstrukcji języka C# na stare tak aby EnterpriseArchtect potrafił to parsować,
tu jest szablon tego konwertera:

```csharp
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
            newRoot = (CSharpSyntaxNode)new ExampleRewriter1().Visit(newRoot);
            newRoot = (CSharpSyntaxNode)new ExampleRewriter2().Visit(newRoot);
            ...
                        
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
    class ExampleRewriter1 : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            ...
        }
        
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            ...
        }
    }

    /// <summary>
    /// Konwertuje inicjalizatory właściwości na tradycyjne deklaracje i inicjalizacje w konstruktorze
    /// </summary>
    class ExampleRewriter2 : CSharpSyntaxRewriter
    {
        
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            ...
        }
        
        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            ...
        }
    }

}


```

szczegółowe założenia:

- użyj Roslyn

- straj się aby poszczególne konwertery nie modyfikowały treśći metod
  bo EnterpriseArchtect interesuje jedynie struktura kodu a nie implementacja,
  chodzi o dokumentację i architekturę
  
- niech konwerter  w miarę możliwości nie modyfikuje formatowania pliku  
  
  
- ad inicjalizacje właściwości bezpośrednio przy deklaracji, np.
    ```
    public string Name { get; set; } = "Unknown";
    ```

  niech konwerter tylko je komentuje:
    ```
    public string Name { get; set; } /* = "Unknown"; */
    ```
    
  taki kod:
    ```
    private ObservableCollection<Inline> _logItems = [];
    ```
    
  na taki:
    ```
    private ObservableCollection<Inline> _logItems; /*= []; */
    ```
    
    
- ad skrócone właściwości:
    ```
    public bool IsAdult => Age >= 18;
    ```

  może być tak poprawiane: 
    ```
    public bool IsAdult { get; } /* => Age >= 18; */
    ```

  np. taki kod:
    ```
    private string _name;
    public string Name 
    { 
        get => _name; 
        set => _name = value?.Trim() ?? ""; 
    }
    ```
  można zamienić na taki:
    ```
    public string Name { get; set; }
    ```
    (pole _name można zostawić)


- ad skrócone metody:
    ```
    public int Add(int a, int b) => a + b;
    ```

  wystarczy że konwerter zakomentuje nowoczesną konstrukcję i wstawi pustą treść:
    ```
    public int Add(int a, int b) { } /* => a + b; */
    ```


- wyrażenia init-only:
    ```
    public string Name { get; init; }
    ```
    
  niech zamienia na :
    ```
    public string Name { get; set; }
    ```


- ad rekordy:
    ```
    public record Person(string Name, int Age);
    ```

  trudna sprawa, jeżeli da radę stworzyć pełną treść to tak, 
  ale jak doda nową treść to niech ją sformatuje

  np.:
    ```
    internal record EqStreamElement<TValue>(TValue Value) : IStreamElement;
    ```

  może zamienić na:
    ```
        internal class EqStreamElement<TValue> : IStreamElement
        {
            public TValue Value { get; private set; }

            public EqStreamElement(TValue value)
            {
                Value = value;
            }
        }
    ```
    
  np.:
    ```
    internal record VarContElement<TValue>(double Delta, TValue Value);
    ```
    
  niech zamieni na:    
    ```
    internal class VarContElement<TValue>
    {
        public double Delta{ get; private set; }
        public TValue Value{ get; private set; }

        public VarContElement(double delta, TValue value)
        {
            Delta = delta;
            Value = value;
        }
    }
    ```

   dla nie-generycznych podobnie, ale nie wiem czy to ma znaczenie..
   
  
