using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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


