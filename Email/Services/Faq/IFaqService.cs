using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Models.Faq;

namespace Email.Services.Faq
{
    public interface IFaqService
    {
        Task<List<FaqCategory>> GetCategoriesWithItemsAsync(CancellationToken ct = default);
        Task<List<FaqAnswer>> GetAnswersAsync(int faqItemId, CancellationToken ct = default);
        Task<int> CreateCategoryAsync(string name, CancellationToken ct = default);
        Task UpdateCategoryAsync(int categoryId, string name, CancellationToken ct = default);
        Task DeleteCategoryAsync(int categoryId, CancellationToken ct = default);
        Task<int> CreateFaqItemAsync(int categoryId, string question, int? createdByUserId, CancellationToken ct = default);
        Task MoveFaqItemAsync(int faqItemId, int newCategoryId, CancellationToken ct = default);
        Task DeleteFaqItemAsync(int faqItemId, CancellationToken ct = default);
        Task<int> PostAnswerAsync(int faqItemId, int authorUserId, string authorName, string content, CancellationToken ct = default);
        Task ToggleAcceptedAsync(int answerId, bool isAccepted, CancellationToken ct = default);
        Task DeleteAnswerAsync(int answerId, CancellationToken ct = default);
    }
}
