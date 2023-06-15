//Code review exercise:
//Suppose you are given the code below to perform a review.
//Write as comments your suggestions or changes requests on the code.

namespace IDB.Business.Preparation.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class OperationController : ControllerBase
    {
        private readonly ISGPrepInScopeAssessorService _inScopeAssessor;
        private readonly ILogger<OperationController> mylogger;
        private readonly IUserContext _userContext;
        private readonly IValidator<InitiationRequestAndContext> _validator;
        private readonly IMediator _mediator;
        public readonly IAsyncRepository<SGPrepOperation> _repo;      
        private readonly IBusinessDateDistanceCalculator _distanceCalculator;
        private readonly IRelativeEastCoastDateTimeConverter _relativeEastCoastDateTimeConverter;
        private readonly IERMDistributionListService _ERMDistributionListService;
        private readonly IOperationWorkingDaysService _operationWorkingDaysService;
        private readonly IOperationInfoService _operationInfoRetriever;
        private readonly IDocumentService _documentService;
        private readonly IMilestonesService _milestonesService;
        private readonly ISGPrepUserRoleService _sgPrepUserRoleService;

        public OperationController(ISGPrepInScopeAssessorService inScopeAssessor,
            ILogger<OperationController> logger,
            IUserContext userContext,
            IMediator mediatr,
            IValidator<InitiationRequestAndContext> validator,
            IAsyncRepository<SGPrepOperation> repo,
            IClock clock,
            IBusinessDateDistanceCalculator distanceCalculator,
            IRelativeEastCoastDateTimeConverter relativeEastCoastDateTimeConverter,
            IERMDistributionListService ERMDistributionListService,
            IOperationWorkingDaysService operationWorkingDaysService,
            IOperationInfoService prepInfoRetriever,
            IDocumentService documentService,
            IMilestonesService milestonesService,
            ISGPrepUserRoleService sgPrepUserRoleService
            )
        {
            _inScopeAssessor = inScopeAssessor;
            mylogger = logger;
            _userContext = userContext;
            _mediator = mediatr;
            _validator = validator;
            _repo = repo;
            _clock = clock;
            _distanceCalculator = distanceCalculator;
            _relativeEastCoastDateTimeConverter = relativeEastCoastDateTimeConverter;
            _ERMDistributionListService = ERMDistributionListService;
            _operationWorkingDaysService = operationWorkingDaysService;
            _operationInfoRetriever = prepInfoRetriever ?? throw new ArgumentNullException(nameof(prepInfoRetriever));
            _documentService = documentService;
            _milestonesService = milestonesService;
            _sgPrepUserRoleService = sgPrepUserRoleService;
        }

        [HttpPost("{opNumber}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(string opNumber)
        {
            var streamId = opNumber.ToOperationStreamIdentifier();

            var operation = await _repo.GetOptionalAsync(streamId);
            if (!operation.HasValue)
            {
                return NotFound();
            }

            var result = new SGPrepOperationView(operation.Value);

            return Ok(result);
        }

        [HttpGet("{opNumber}/projectProfile")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        //[Authorize(SGPrepPolicies.CorrectOperationRolePolicy)]
        public async Task<IActionResult> GetProjectProfileTemplate(string opNumber, [FromQuery] string lang)
        {
            var result = await _operationInfoRetriever.GetProjectProfileTemplatePreview(opNumber, lang.ToLower());
            return Ok(result);
        }    

        [HttpGet("{opNumber}/Code")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        //[Authorize(SGPrepPolicies.CorrectOperationRolePolicy)]
        public async Task<IActionResult> GetCode(string opNumber)
        {
            return Ok(await _operationInfoRetriever.GetLoanModalityCode(opNumber));
        }

        [HttpGet("{opNumber}/roles/me")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetUserRoles(string opNumber)
        {
            ValidateUserContext();

            try
            {
                var roles = await _sgPrepUserRoleService.GetUserRoles(_userContext.Name, opNumber);
                return Ok(roles);
            }
            catch (Exception ex)
            {                
                throw;
            }

        }    
        
        [HttpGet("{opNumber}/{code}/eligibility")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [Authorize(SGPrepPolicies.CorrectOperationRolePolicy)]
        public async Task<IActionResult> GetEligibility(string opNumber)
        {
            ValidateUserContext();

            var streamId = opNumber.ToOperationStreamIdentifier();
            var operation = await _repo.GetOptionalAsync(streamId);

            if (!operation.HasValue)
            {
                _logger.LogWarning("Operation {OperationNumber} was not found in the SG Prep event store using stream Id {OperationStreamID}", opNumber, streamId);
                return NotFound();
            }
            
            var view = new SGPrepOperationView(operation.Value);

            if (view.EligibilitySubmission is null)
            {
                _logger.LogWarning("No eligibility submission on the view model for {OperationNumber}", opNumber);
                return NotFound();
            }

            return Ok(new EligibilitySubmissionResponse
            {
                CirculationPeriod = new EligibilityCirculationPeriod
                {
                    StartDate = view.EligibilitySubmission.StartDate?.ToString("yyyy-MM-dd"),
                    EndDate = view.EligibilitySubmission.EndDate?.ToString("yyyy-MM-dd"),
                    Meeting = new EligibilityMeeting
                    {
                        Date = view.EligibilitySubmission.MeetingStartTime?.Date.ToString("yyyy-MM-dd"),
                        EndTime = !view.EligibilitySubmission.EndTimeHasBeenSet ? null : (DateTime.Today + view.EligibilitySubmission.MeetingEndTime.Value.TimeOfDay).ToString("h:mm tt"),
                        Justification = view.EligibilitySubmission.MeetingJustification,
                        MeetingLink = view.EligibilitySubmission.MeetingLink,
                        StartTime = !view.EligibilitySubmission.StartTimeHasBeenSet ? null : (DateTime.Today + view.EligibilitySubmission.MeetingStartTime.Value.TimeOfDay).ToString("h:mm tt")
                    }
                },
                DocPreparationProcessing = view.EligibilitySubmission.IsIcapPaciRequired != null ?
                    new EligibilityDocPreparationProcessing
                    {
                        IsIcapPaciRequired = view.EligibilitySubmission.IsIcapPaciRequired,
                        ProcessingTrack = view.EligibilitySubmission.GetProcessingTrack()
                    } : null
            });
        }

        [HttpPut("{opNumber}/eligibility")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        //[Authorize(SGPrepPolicies.CorrectOperationRolePolicy)]
        public async Task<IActionResult> SaveEligibility([FromBody] EligibilitySubmissionRequest request, string opNumber)
        {
            ValidateUserContext();
            
            var streamId = opNumber.ToOperationStreamIdentifier();
            var existingOperation = await _repo.GetOptionalAsync(streamId);

            if (!existingOperation.HasValue)
            {
                return NotFound();
            }

            if (existingOperation.Value.EligibilitySubmission is not null)
            {
                throw new EligibilitySubmissionModelInvalidException("An eligibility submission already exists for this operation");
            }

            string resultValidation = string.empty;

            switch (request.DocType)
            {
                case "PP":
                    resultValidation = await ValidatePPSubmissionRequestAsync(request, opNumber);
                    switch (request.DocVersion)
                    {                        
                       case "Approval":
                        await SendAproval(opNumber);
                        break;
                       case "Revision":
                        await SendRevision(opNumber);
                    }
                break;
                case "Minutes":
                    resultValidation = await ValidateMinutesSubmissionRequestAsync(request, opNumber);
                break;
                case "Annex":
                    resultValidation = await ValidateAnnexSubmissionRequestAsync(request, opNumber);
                break;                
            }  

            if (!string.IsNullOrEmpty(resultValidation))
            {
                return BadRequest(resultValidation);
            }

            PrepareEligibilitySubmissionForSaving(request, existingOperation.Value);

            await _repo.SaveAllUnitsOfWorkAsync();

            return Created(nameof(GetEligibility), existingOperation.Value.EligibilitySubmission);
        }

        [HttpPost("{opNumber}/eligibility")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        //[Authorize(SGPrepPolicies.CorrectOperationRolePolicy)]
        public async Task<IActionResult> UpdateEligibility([FromBody] EligibilitySubmissionRequest request, string opNumber)
        {
            // TODO: Future -- create UserContextValidator and then can make the validator below a composite validator that includes that validator
            ValidateUserContext();

            var streamId = opNumber.ToOperationStreamIdentifier();
            var existingOperation = await _repo.GetOptionalAsync(streamId);

            if (!existingOperation.HasValue)
            {
                return NotFound();
            }

            if (existingOperation.Value.EligibilitySubmission is null)
            {
                return NotFound("The Eligibility Submission doesn't exist.");
            }

            string resultValidation = await ValidateElegibilitySubmissionRequestAsync(request, opNumber);

            if (!string.IsNullOrEmpty(resultValidation))
            {
                return BadRequest(resultValidation);
            }

            PrepareEligibilitySubmissionForSaving(request, existingOperation.Value);
            await _repo.SaveAllUnitsOfWorkAsync();

            return Ok();   
        }             
        
        [HttpPost("{opNumber}/documents/projectProfile/disclosure")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        //[Authorize(SGPrepPolicies.CorrectOperationRolePolicy)]
        public async Task<IActionResult> DiscloseDocument(string opNumber)
        {
            if (string.IsNullOrWhiteSpace(opNumber))
            {
                return BadRequest("Operation Number has not been provided");
            }

            ValidateUserContext();

            var operation = await GetOperationByOperationNumber(opNumber);

            if (operation.PPDisclosedDocuments.Exists(doc => doc.DocumentType.GetEnumValueFromDescription<DocumentType>() == DocumentType.PublicPPPdf))
            {
                throw new Exception("Public project profile has already been disclosed");
            }
            
            var publicPPPdf = operation.PPDocuments.FirstOrDefault(doc => 
                doc.DocumentType.GetEnumValueFromDescription<DocumentType>() == DocumentType.PublicPPPdf);

            if (publicPPPdf is null)
            {
                return NotFound($"Could not find a Public Project Profile for {opNumber}");
            }
            
            await _documentService.DiscloseDocumentAsync(opNumber, publicPPPdf);

            await _mediator.Send(new UpdatePPPDMilestoneCommand(opNumber));

            return Ok();
        }

        private void ValidateUserContext()
        {
            if (string.IsNullOrWhiteSpace(_userContext.Name) || string.IsNullOrWhiteSpace(_userContext.UUID))
            {
                _logger.LogError("Could not determine user context. Name: {UserContextName}, UUID: {UserContextUUID}", _userContext.Name, _userContext.UUID);
                throw new Exception("We encountered an issue determining user context information for the request");
            }
        }

        private async Task<BadRequestObjectResult?> ValidateCirculationDates(DateOnly? circulationPeriodEndDate, string date, string opNumber)
        {
            if (circulationPeriodEndDate is null)
            {
                return BadRequest("There is no end date set for the circulation period in this operation");
            }

            if (string.IsNullOrWhiteSpace(date))
            {
                return BadRequest("The date of the meeting is needed");
            }

            if (!await _operationWorkingDaysService.IsWorkingDay(opNumber, DateTime.Parse(date).ToDateOnly()))
            {
                return BadRequest("The date selected is not a working day");
            }

            return null;
        }

        private async Task<string> ValidateElegibilitySubmissionRequestAsync(EligibilitySubmissionRequest request, string opNumber)
        {
            if (request.CirculationPeriod.IsNotNull())
            {
                var validator = new EligibilitySubmissionCirculationPeriodValidator(_operationWorkingDaysService, _distanceCalculator, opNumber, request.CirculationPeriod.StartDate, request.CirculationPeriod.EndDate);
                var result = await validator.ValidateAsync(request);

                if (!result.IsValid)
                {
                    return result.ErrorsAsString();
                }
            }

            return string.Empty;
        }

        private async Task<SGPrepOperation> GetOperationByOperationNumber(string opNumber)
        {
            var streamId = opNumber.ToOperationStreamIdentifier();

            var operation =  await _repo.GetOptionalAsync(streamId);

            if (!operation.HasValue)
            {
                _logger.LogWarning("Operation {OperationNumber} was not found in the SG Prep event store using stream Id {OperationStreamID}", opNumber, streamId);
                throw new OperationNotInitiatedException(opNumber);
            }

            return operation.Value;
        }

        private void PrepareEligibilitySubmissionForSaving(EligibilitySubmissionRequest request, SGPrepOperation existingOperation)
        {
            existingOperation.ChangeEligibilitySubmission(
            _clock,
            _userContext.UUID,
            string.IsNullOrEmpty(request.CirculationPeriod?.StartDate) ? null : DateOnly.Parse(request.CirculationPeriod?.StartDate),
            string.IsNullOrEmpty(request.CirculationPeriod?.EndDate) ? null : DateOnly.Parse(request.CirculationPeriod?.EndDate),
            request.CirculationPeriod?.Meeting?.Justification?.Trim(),
            !string.IsNullOrEmpty(request?.CirculationPeriod?.Meeting?.StartTime),
            !string.IsNullOrEmpty(request?.CirculationPeriod?.Meeting?.EndTime),
            GetMeetingDateTimeOffset(request?.CirculationPeriod?.Meeting?.Date, request?.CirculationPeriod?.Meeting?.StartTime),
            GetMeetingDateTimeOffset(request?.CirculationPeriod?.Meeting?.Date, request?.CirculationPeriod?.Meeting?.EndTime),
            request.CirculationPeriod?.Meeting?.MeetingLink?.Trim(),
            request.DocPreparationProcessing?.IsIcapPaciRequired,
            request.DocPreparationProcessing?.GetProcessingTrackName()
            );
        }

        private void PrepareEligibilityReviewMeetingForSaving(EligibilityReviewMeetingRequest request, SGPrepOperation existingOperation, DateOnly circulationPeriodEndDate)
        {
            existingOperation.ChangeEligibilityReviewMeeting(
            _userContext.UUID,
            DateTime.Parse(request.Date).ToDateOnly(),
            circulationPeriodEndDate
            );
        }
    }
}
