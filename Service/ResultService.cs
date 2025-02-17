using SpoerStats2.Models;
using SpoerStats2.Repository;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class ResultService
{
    private readonly IResultRepository _resultRepository;
    private readonly IUserRepository _userRepository;

    public ResultService(IResultRepository resultRepository, IUserRepository userRepository)
    {
        _resultRepository = resultRepository;
        _userRepository = userRepository;
    }

    public async Task<IEnumerable<Result>> GetAllResults()
    {
        return await _resultRepository.GetAllResults();
    }

    public async Task<Result> GetResultById(int id, int requesterId, int roleId, int clubId)
    {
        var result = await _resultRepository.GetResultById(id);
        if (result == null)
        {
            throw new KeyNotFoundException("Резултатът не е намерен.");
        }

        if (roleId == 2) // Coach
        {
            var targetUser = await _userRepository.GetUserById(result.UserId);
            if (targetUser == null || targetUser.ClubID != clubId)
            {
                throw new UnauthorizedAccessException("Нямате право да видите този резултат.");
            }
        }
        else if (result.UserId != requesterId) // Regular User
        {
            throw new UnauthorizedAccessException("Нямате право да видите този резултат.");
        }

        return result;
    }
    public async Task<IEnumerable<Result>> GetResultsForClub(int clubId)
    {
        var clubUsers = await _userRepository.GetUsersByClubId(clubId);
        var userIds = clubUsers.Select(u => u.Id);

        return await _resultRepository.GetResultsByUserIds(userIds);
    }



    public async Task<IEnumerable<Result>> GetResultsForUser(int requesterId, int roleId, int clubId, int? targetUserId = null)
    {
        if (roleId == 2) // Coach
        {
            if (targetUserId.HasValue)
            {
                var targetUser = await _userRepository.GetUserById(targetUserId.Value);
                if (targetUser != null && targetUser.ClubID == clubId)
                {
                    return await _resultRepository.GetResultsByUserId(targetUserId.Value);
                }
                else
                {
                    throw new UnauthorizedAccessException("Нямате достъп до резултатите на този потребител.");
                }
            }
            else
            {
                var clubUsers = await _userRepository.GetUsersByClubId(clubId);
                var userIds = clubUsers.Select(u => u.Id);
                return await _resultRepository.GetResultsByUserIds(userIds);
            }
        }
        else // Regular User
        {
            return await _resultRepository.GetResultsByUserId(requesterId);
        }
    }

    public async Task AddResult(Result result, int requesterId, int roleId, int clubId)
    {
        if (result.ValueTime < 0 || result.ValueTime > 86400)
        {
            throw new ArgumentException("Невалидна стойност за време.");
        }

        if (roleId == 2)
        {
            var targetUser = await _userRepository.GetUserById(result.UserId);
            if (targetUser == null || targetUser.ClubID != clubId)
            {
                throw new UnauthorizedAccessException("Нямате право да добавяте резултати за този потребител.");
            }
        }
        else if (result.UserId != requesterId)
        {
            throw new UnauthorizedAccessException("Нямате право да добавяте резултати за друг потребител.");
        }

        await _resultRepository.AddResult(result);
    }

    public async Task UpdateResult(Result result, int requesterId, int roleId, int clubId)
    {
        var existingResult = await _resultRepository.GetResultById(result.Id);
        if (existingResult == null)
        {
            throw new KeyNotFoundException("Резултатът не е намерен.");
        }

        if (roleId == 2)
        {
            var targetUser = await _userRepository.GetUserById(existingResult.UserId);
            if (targetUser == null || targetUser.ClubID != clubId)
            {
                throw new UnauthorizedAccessException("Нямате право да редактирате този резултат.");
            }
        }
        else if (existingResult.UserId != requesterId)
        {
            throw new UnauthorizedAccessException("Нямате право да редактирате този резултат.");
        }

        await _resultRepository.UpdateResult(result);
    }

    public async Task DeleteResult(int id, int requesterId, int roleId, int clubId)
    {
        var result = await _resultRepository.GetResultById(id);
        if (result == null)
        {
            throw new KeyNotFoundException("Резултатът не е намерен.");
        }

        if (roleId == 2)
        {
            var targetUser = await _userRepository.GetUserById(result.UserId);
            if (targetUser == null || targetUser.ClubID != clubId)
            {
                throw new UnauthorizedAccessException("Нямате право да изтривате този резултат.");
            }
        }
        else if (result.UserId != requesterId)
        {
            throw new UnauthorizedAccessException("Нямате право да изтривате този резултат.");
        }

        await _resultRepository.DeleteResult(id);
    }

    public async Task<IEnumerable<Result>> GetResultsByUserIdAndDisciplineId(int userId, int disciplineId)
    {
        var results = await _resultRepository.GetResultsByUserId(userId);
        return results.Where(r => r.DisciplineId == disciplineId);
    }

    public async Task<IEnumerable<Result>> GetResultsByUserId(int userId)
    {
        return await _resultRepository.GetResultsByUserId(userId);
    }
}
