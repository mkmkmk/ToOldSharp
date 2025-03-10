using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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


