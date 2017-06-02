using Orchard.DisplayManagement;
using Orchard.DisplayManagement.Descriptors;

namespace Orchard.Markdown
{
    public class MediaShapes : IShapeTableProvider
    {
        public void Discover(ShapeTableBuilder builder)
        {
            builder.Describe("Markdown_Editor")
                .OnDisplaying(displaying =>
                {
                    IShape editor = displaying.Shape;

                    if (editor.Metadata.Alternates.Contains("Markdown_Editor__Wysiwyg"))
                    {
                        editor.Metadata.Wrappers.Add("Media_Wrapper__Markdown");
                    }
                });
        }
    }
}