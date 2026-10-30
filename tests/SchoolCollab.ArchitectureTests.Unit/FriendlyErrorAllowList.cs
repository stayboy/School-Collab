namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// <b>The ratchet ledger for the friendly-error guard</b> — one entry per raw
/// <c>Exception.Message</c> site that remains in a <c>.razor</c> file, in the shape
/// <c>file:line</c> plus a one-line reason. Nothing is grandfathered by silence: every site the scan
/// finds must appear here, and the count may never exceed <see cref="RecordedEntryCount"/>.
///
/// <para><b>Why a ledger instead of a big-bang sweep.</b> The defect (<c>_error = ex.Message</c> inside
/// a <c>.razor</c>) is a pattern, not a handful of spots: the round that introduced the guard counted
/// over three hundred of them, each of which compiles, renders and reviews cleanly because the leak only
/// shows the day an internal error reaches a user's screen. Converting them all in one change would put
/// untested pages at risk for a cosmetic gain, so the round converted the four surfaces the #416 report
/// and its siblings name, and this file pins the rest: the guard fails immediately on a NEW site, and
/// the count assertion fails if someone appends a line to admit one.</para>
///
/// <para><b>Shrinking is the only legal edit.</b> Convert a site and delete its line, then lower
/// <see cref="RecordedEntryCount"/> to the new length in the same change — the two edits together are
/// the visible act of paying debt down. Adding a line is not legal, and neither is raising the count
/// without converting a site.</para>
///
/// <para><b>Matching is per (file, ordinal), not per line number.</b> The recorded line keeps each
/// entry honest and locatable, but an unrelated edit elsewhere in one of these files must not redden a
/// ratchet it did not break; what the guard enforces is that each file holds exactly the number of
/// allow-listed sites it still holds. A conversion therefore shows up as a count mismatch and demands
/// the line be deleted, which is the behaviour the ratchet wants.</para>
///
/// <para>Reasons are the site's shape, not a per-site essay: the migration target is the same
/// everywhere — <c>FriendlyError.Describe</c> for the text,
/// <c>FriendlyErrorBar</c> for the render.</para>
/// </summary>
internal static class FriendlyErrorAllowList
{
    /// <summary>
    /// The number of raw sites recorded when the guard landed (debt-sweep round, 2026-10-27). A site
    /// count above this fails: admitting one means converting one first.
    /// </summary>
    public const int RecordedEntryCount = 288;

    /// <summary>Every remaining raw <c>Exception.Message</c> site, as <c>file:line</c>.</summary>
    public static readonly string[] Sites =
    [
        "src/AI/SchoolCollab.AI.Chat/Components/AiChat.razor:165", // error field assigned the raw message
        "src/AI/SchoolCollab.AI.Chat/Components/AiChat.razor:648", // error field assigned the raw message

        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor:765", // ErrorBoundary content interpolates the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor:2278", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor:3714", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor:3837", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor:3869", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor:3901", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor:3933", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/AssignmentAuthoring.razor:3964", // error field assigned the raw message


        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:203", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:264", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:291", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:318", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:343", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:373", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:406", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:618", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Index.razor:643", // error field assigned the raw message

        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/NotificationFailuresSection.razor:143", // error field assigned the raw message

        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionGenerationSection.razor:437", // error field assigned the raw message

        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/QuestionsDraftSection.razor:458", // error field assigned the raw message

        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/RecorderPanel.razor:357", // recorder maps the raw capture error text
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/RecorderPanel.razor:392", // recorder maps the raw capture error text
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/RecorderPanel.razor:422", // recorder maps the raw capture error text
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/RecorderPanel.razor:473", // recorder maps the raw capture error text

        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor:235", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/ResourcesSection.razor:368", // error field assigned the raw message

        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOff.razor:138", // ErrorBoundary content interpolates the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOff.razor:172", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOff.razor:203", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOff.razor:232", // error field assigned the raw message

        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOffSection.razor:151", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOffSection.razor:187", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOffSection.razor:209", // error field assigned the raw message
        "src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOffSection.razor:233", // error field assigned the raw message

        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor:85", // error field assigned the raw message
        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor:131", // error field assigned the raw message
        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor:135", // error field assigned the raw message
        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Spike.razor:143", // error field assigned the raw message

        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/Detail.razor:218", // ErrorBoundary content interpolates the raw message
        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/Detail.razor:302", // error field assigned the raw message
        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/Detail.razor:308", // error field assigned the raw message

        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/Index.razor:203", // error field assigned the raw message
        "src/Mobile/SchoolCollab.Mobile/Components/Pages/Assignments/Index.razor:209", // error field assigned the raw message

        "src/SchoolCollab.Admin/Components/Layout/DevTenantSwitcher.razor:155", // error field assigned the raw message

        "src/SchoolCollab.Admin.Shared/Components/CodedValueDialog.razor:172", // dialog footer error set from the raw message

        "src/SchoolCollab.Admin.Shared/Components/Landing/LandingPage.razor:80", // ErrorBoundary content interpolates the raw message

        "src/SchoolCollab.Families/Components/Pages/Ward/Assignment.razor:187", // ErrorBoundary content interpolates the raw message
        "src/SchoolCollab.Families/Components/Pages/Ward/Assignment.razor:297", // error field assigned the raw message
        "src/SchoolCollab.Families/Components/Pages/Ward/Assignment.razor:391", // error field assigned the raw message
        "src/SchoolCollab.Families/Components/Pages/Ward/Assignment.razor:414", // error field assigned the raw message
        "src/SchoolCollab.Families/Components/Pages/Ward/Assignment.razor:441", // error field assigned the raw message

        "src/SchoolCollab.Families/Components/Pages/Ward/Index.razor:80", // ErrorBoundary content interpolates the raw message
        "src/SchoolCollab.Families/Components/Pages/Ward/Index.razor:135", // error field assigned the raw message

        "src/SchoolCollab.Families/Components/Pages/Ward/SignOff.razor:137", // ErrorBoundary content interpolates the raw message
        "src/SchoolCollab.Families/Components/Pages/Ward/SignOff.razor:293", // wording branch reads the raw message to pick text

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/AssignmentAiPrompt.razor:63", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/AssignmentAiPrompt.razor:87", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/AssignmentAiPrompt.razor:113", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlagDetail.razor:118", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlagDetail.razor:161", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/SignatureConsentText.razor:50", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/SignatureConsentText.razor:77", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/SignatureConsentText.razor:101", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Approvals.razor:82", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Approvals.razor:109", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Approvals.razor:130", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Children.razor:174", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Children.razor:202", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Children.razor:241", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Children.razor:247", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Children.razor:300", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Create.razor:46", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Create.razor:116", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Edit.razor:194", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Edit.razor:275", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Edit.razor:295", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Edit.razor:352", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Edit.razor:383", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Edit.razor:431", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Edit.razor:460", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Edit.razor:478", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Index.razor:187", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Index.razor:204", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Index.razor:337", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Index.razor:370", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Index.razor:404", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Index.razor:411", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/CodedValues/Index.razor:469", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlags/Create.razor:63", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlags/Create.razor:103", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlags/Index.razor:124", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Create.razor:64", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Create.razor:175", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Create.razor:180", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Create.razor:187", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Edit.razor:188", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Edit.razor:270", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Edit.razor:290", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Edit.razor:359", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Edit.razor:364", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Edit.razor:371", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Edit.razor:500", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Edit.razor:506", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Index.razor:131", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Index.razor:160", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/Index.razor:192", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/OverrideEditor.razor:180", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/OverrideEditor.razor:236", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/EntityCodeRules/OverrideEditor.razor:249", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/Index.razor:286", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/Index.razor:496", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/Index.razor:524", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/TemplateEditor.razor:347", // ErrorBoundary content interpolates the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/TemplateEditor.razor:621", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/TemplateEditor.razor:768", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/TemplateEditor.razor:880", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/TemplateEditor.razor:1118", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/NotificationTemplates/TemplateEditor.razor:1161", // error field assigned the raw message

        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor:163", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor:198", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor:237", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor:315", // error field assigned the raw message
        "src/Settings/SchoolCollab.Settings.Application/Components/Pages/Tenants/Index.razor:337", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor:615", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor:715", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor:835", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor:933", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor:996", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor:1015", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Contacts/ContactsEditor.razor:1038", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor:190", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor:222", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor:245", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor:265", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor:285", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor:310", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroups.razor:290", // wording branch reads the raw message to pick text

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Guardians/GuardianDetail.razor:187", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Guardians/GuardianDetail.razor:269", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Guardians/GuardianDetail.razor:319", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Guardians/GuardianDetail.razor:348", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Guardians/GuardianDetail.razor:373", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Guardians/Guardians.razor:190", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Guardians/Guardians.razor:220", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodSubPeriodsEditor.razor:312", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodSubPeriodsEditor.razor:442", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodSubPeriodsEditor.razor:557", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodSubPeriodsEditor.razor:609", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodUpsert.razor:191", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodUpsert.razor:354", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodUpsert.razor:395", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodUpsert.razor:501", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodUpsert.razor:550", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodUpsert.razor:584", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodUpsert.razor:626", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor:137", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor:198", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor:225", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor:254", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor:284", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor:312", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor:336", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor:66", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor:104", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor:174", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor:190", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor:211", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor:148", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor:173", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor:202", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor:225", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Create.razor:142", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Create.razor:229", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Create.razor:289", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Create.razor:308", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Create.razor:319", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:333", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:539", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:643", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:737", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:784", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:803", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:886", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:919", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:962", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:994", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor:1016", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:242", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:387", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:418", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:441", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:546", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:655", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:756", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:775", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:807", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor:844", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Index.razor:299", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Index.razor:369", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Index.razor:409", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Index.razor:455", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Index.razor:481", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Index.razor:506", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Create.razor:79", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Create.razor:117", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Create.razor:136", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Create.razor:174", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Create.razor:198", // per-topic warning line built from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:258", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:471", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:593", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:615", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:734", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:772", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:825", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:848", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:945", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:1051", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:1151", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:1165", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:1179", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:1197", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:1332", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor:1354", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Edit.razor:93", // ErrorBoundary content interpolates the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Edit.razor:153", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Edit.razor:193", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Edit.razor:217", // per-topic warning line built from the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Edit.razor:230", // per-topic warning line built from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Index.razor:244", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Index.razor:296", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Index.razor:329", // wording branch reads the raw message to pick text
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Index.razor:331", // wording branch reads the raw message to pick text

        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor:257", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor:357", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor:385", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor:651", // wording branch reads the raw message to pick text
        "src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Subjects/Subjects.razor:653", // wording branch reads the raw message to pick text

        "src/Students/SchoolCollab.Students.Application/Components/Students/AssignmentPolicyEditor.razor:106", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/AssignmentPolicyEditor.razor:145", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/AssignmentPolicyEditor.razor:169", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/EnrollStudentDialog.razor:645", // dialog footer error set from the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/EnrollStudentDialog.razor:695", // dialog footer error set from the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/EnrollStudentDialog.razor:742", // dialog footer error set from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor:439", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor:666", // dialog footer error set from the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor:702", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeLevelCreateDialog.razor:162", // dialog footer error set from the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeLevelCreateDialog.razor:262", // per-topic warning line built from the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeLevelCreateDialog.razor:277", // per-topic warning line built from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeLevelEditDialog.razor:204", // dialog footer error set from the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeLevelEditDialog.razor:320", // per-topic warning line built from the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeLevelEditDialog.razor:333", // per-topic warning line built from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeNotificationPolicyEditor.razor:108", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeNotificationPolicyEditor.razor:147", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeNotificationPolicyEditor.razor:171", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/GradeTopicsDialog.razor:310", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor:1116", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor:1238", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor:1304", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor:1396", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor:1570", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor:1619", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor:1695", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor:1825", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardiansTab.razor:149", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardiansTab.razor:172", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/GuardiansTab.razor:194", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/JoinGroupsDialog.razor:208", // per-group failure line built from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/NotificationTemplatesCard.razor:99", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/SectionListDialog.razor:206", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/StrandsEditor.razor:159", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/StrandsEditor.razor:201", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/StrandsEditor.razor:218", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/StrandsEditor.razor:249", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/StrandsEditor.razor:263", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/StudentCreateDialog.razor:77", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor:230", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor:264", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/StudentPickerDialog.razor:220", // error field assigned the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/StudentTransferDialog.razor:202", // error field assigned the raw message
        "src/Students/SchoolCollab.Students.Application/Components/Students/StudentTransferDialog.razor:251", // dialog footer error set from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/TeacherEditDialog.razor:334", // dialog footer error set from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/TopicCreateDialog.razor:393", // dialog footer error set from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/TopicEditDialog.razor:241", // dialog footer error set from the raw message

        "src/Students/SchoolCollab.Students.Application/Components/Students/WithdrawEnrollmentDialog.razor:96", // dialog footer error set from the raw message
    ];
}
