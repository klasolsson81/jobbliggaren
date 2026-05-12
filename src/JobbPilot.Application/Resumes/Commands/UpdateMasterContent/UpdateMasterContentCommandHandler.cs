using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Application.Resumes.Queries;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Resumes.Commands.UpdateMasterContent;

public sealed class UpdateMasterContentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<UpdateMasterContentCommand, Result>
{
    public async ValueTask<Result> Handle(
        UpdateMasterContentCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resumeId = new ResumeId(command.ResumeId);
        var resume = await db.Resumes
            .Include(r => r.Versions)
            .FirstOrDefaultAsync(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId, cancellationToken);

        if (resume is null)
        {
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "UpdateMasterContent");
            }
            throw new NotFoundException("CV hittades inte.");
        }

        var content = MapToDomain(command.Content);
        return resume.UpdateMasterContent(content, clock);
    }

    private static ResumeContent MapToDomain(ResumeContentDto dto)
    {
        var personalInfo = new PersonalInfo(
            dto.PersonalInfo.FullName,
            dto.PersonalInfo.Email,
            dto.PersonalInfo.Phone,
            dto.PersonalInfo.Location);

        var experiences = dto.Experiences
            .Select(e => new Experience(e.Company, e.Role, e.StartDate, e.EndDate, e.Description))
            .ToList();

        var educations = dto.Educations
            .Select(e => new Education(e.Institution, e.Degree, e.StartDate, e.EndDate))
            .ToList();

        var skills = dto.Skills
            .Select(s => new Skill(s.Name, s.YearsExperience))
            .ToList();

        return new ResumeContent(personalInfo, experiences, educations, skills, dto.Summary);
    }
}
