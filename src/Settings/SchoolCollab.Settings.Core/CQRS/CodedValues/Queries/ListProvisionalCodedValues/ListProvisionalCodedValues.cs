using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListProvisionalCodedValues;

/// <summary>
/// Lists coded values pending system-wide approval (tcv/3). These are tenant-owned
/// rows with <c>IsProvisional = true</c>, surfaced to the Settings admin approval
/// queue. Cross-tenant by design (the "Tenant" filter is ignored).
/// </summary>
public sealed record ListProvisionalCodedValues : IQuery<CodedValueDto[]>;
