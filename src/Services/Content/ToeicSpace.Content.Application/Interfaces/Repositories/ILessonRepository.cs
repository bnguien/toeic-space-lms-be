using ToeicSpace.Content.Domain.Entities;

namespace ToeicSpace.Content.Application.Interfaces.Repositories;

public interface ILessonRepository
{
    Task<bool> IsObjectKeyUsedAsync(string key, CancellationToken ct);
}
