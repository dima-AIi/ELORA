using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class FaqModel : PageModel
{
    private readonly ContentRepository _content;

    public FaqModel(ContentRepository content) => _content = content;

    public List<FaqItem> Faq { get; private set; } = new();

    public void OnGet() => Faq = _content.GetFaq();
}
