using SpoerStats2.Models;
using SpoerStats2.Repository;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpoerStats2.Service
{
    public class ClubService
    {
        private readonly IClubRepository _clubRepository;

        public ClubService(IClubRepository clubRepository)
        {
            _clubRepository = clubRepository;
        }

        private static bool IsAdmin(int roleId) => roleId == 3;

        private static void EnsureAdmin(int requesterRoleId)
        {
            if (!IsAdmin(requesterRoleId))
                throw new UnauthorizedAccessException("Само админ може да управлява клубове.");
        }

        public Task<IEnumerable<Club>> GetAllClubs()
            => _clubRepository.GetAllClubs();

        public Task<Club> GetClubById(int id)
            => _clubRepository.GetClubById(id);

        // ✅ Admin-only
        public Task AddClub(Club club, int requesterRoleId)
        {
            EnsureAdmin(requesterRoleId);
            return _clubRepository.AddClub(club);
        }

        // ✅ Admin-only
        public Task UpdateClub(Club club, int requesterRoleId)
        {
            EnsureAdmin(requesterRoleId);
            return _clubRepository.UpdateClub(club);
        }

        // ✅ Admin-only
        public Task DeleteClub(int id, int requesterRoleId)
        {
            EnsureAdmin(requesterRoleId);
            return _clubRepository.DeleteClub(id);
        }

        // LENEX helpers (read-only)
        public Task<Club?> GetClubByCodeNation(string code, string nation)
            => _clubRepository.GetClubByCodeNation(code, nation);

        public Task<Club?> FindClubByNameLoose(string name)
            => _clubRepository.FindClubByNameLoose(name);
    }
}