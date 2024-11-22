using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using System.ComponentModel.DataAnnotations;

namespace JuegoFramework.Helpers
{
    public class RequiredBindingMetadataProvider : IBindingMetadataProvider
    {
        public void CreateBindingMetadata(BindingMetadataProviderContext context)
        {
            if (context.PropertyAttributes != null && context.PropertyAttributes.OfType<RequiredAttribute>().Any())
            {
                context.BindingMetadata.IsBindingRequired = true;
            }
        }
    }
}
