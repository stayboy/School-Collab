using FluentAssertions;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.AI.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Tests for <see cref="AssignmentQuestionResponseParser"/> against the spec
/// §4.3 schema (one MC + one TF + one shortAnswer) + every rejection path.
/// </summary>
[TestClass]
public class AssignmentQuestionResponseParserTests
{
    private const string SpecExample = """
        {
          "questions": [
            {
              "text": "Which organelle performs photosynthesis?",
              "type": "multipleChoice",
              "options": [
                {"text": "Mitochondria", "isCorrect": false},
                {"text": "Chloroplast", "isCorrect": true}
              ],
              "modelAnswer": null
            },
            {
              "text": "Photosynthesis requires sunlight.",
              "type": "trueFalse",
              "options": [
                {"text": "True", "isCorrect": true},
                {"text": "False", "isCorrect": false}
              ],
              "modelAnswer": null
            },
            {
              "text": "Name the main product of photosynthesis.",
              "type": "shortAnswer",
              "options": null,
              "modelAnswer": "Glucose"
            }
          ]
        }
        """;

    [TestMethod]
    public void Parse_SpecExample_ReturnsValidatedResponse()
    {
        var response = AssignmentQuestionResponseParser.Parse(SpecExample);

        response.Questions.Should().HaveCount(3);
        response.Questions[0].Type.Should().Be(GeneratedQuestionType.MultipleChoice);
        response.Questions[0].Options.Should().HaveCount(2);
        response.Questions[0].Options!.Single(o => o.IsCorrect).Text.Should().Be("Chloroplast");
        response.Questions[1].Type.Should().Be(GeneratedQuestionType.TrueFalse);
        response.Questions[1].Options.Should().HaveCount(2);
        response.Questions[2].Type.Should().Be(GeneratedQuestionType.ShortAnswer);
        response.Questions[2].ModelAnswer.Should().Be("Glucose");
    }

    [TestMethod]
    public void Parse_FencedJsonCodeBlock_StripsFence()
    {
        var fenced = "```json\n" + SpecExample + "\n```";

        var response = AssignmentQuestionResponseParser.Parse(fenced);

        response.Questions.Should().HaveCount(3);
    }

    [TestMethod]
    public void Parse_ProseWrappedJson_ExtractsObject()
    {
        var wrapped = "Here are the questions:\n" + SpecExample + "\nThat is all.";

        var response = AssignmentQuestionResponseParser.Parse(wrapped);

        response.Questions.Should().HaveCount(3);
    }

    [TestMethod]
    public void Parse_UnknownExtraProperties_AreTolerated()
    {
        var withExtras = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "shortAnswer",
                  "options": null,
                  "modelAnswer": "A",
                  "trailing": {"foo": "bar"},
                  "tags": ["a", "b"]
                }
              ],
              "model": "test-model"
            }
            """;

        var response = AssignmentQuestionResponseParser.Parse(withExtras);

        response.Questions.Should().HaveCount(1);
        response.Questions[0].ModelAnswer.Should().Be("A");
    }

    [TestMethod]
    public void Parse_MalformedJson_ThrowsWith502()
    {
        var act = () => AssignmentQuestionResponseParser.Parse("{ not json");

        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   ", DisplayName = "whitespace")]
    public void Parse_EmptyOrWhitespace_ThrowsWith502(string text)
    {
        var act = () => AssignmentQuestionResponseParser.Parse(text);

        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_EmptyQuestionsArray_Throws()
    {
        var act = () => AssignmentQuestionResponseParser.Parse("""{"questions": []}""");
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_BlankQuestionText_Throws()
    {
        var payload = """
            {"questions":[{"text":"","type":"shortAnswer","options":null,"modelAnswer":null}]}
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_UnknownTypeString_Throws()
    {
        var payload = """
            {"questions":[{"text":"Q?","type":"essay","options":null,"modelAnswer":null}]}
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_MultipleChoiceNoCorrectOption_Throws()
    {
        var payload = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "multipleChoice",
                  "options": [
                    {"text": "A", "isCorrect": false},
                    {"text": "B", "isCorrect": false}
                  ]
                }
              ]
            }
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_MultipleChoiceTwoCorrectOptions_Throws()
    {
        var payload = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "multipleChoice",
                  "options": [
                    {"text": "A", "isCorrect": true},
                    {"text": "B", "isCorrect": true}
                  ]
                }
              ]
            }
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_MultipleChoiceTooFewOptions_Throws()
    {
        var payload = """
            {"questions":[{"text":"Q?","type":"multipleChoice","options":[{"text":"Only","isCorrect":true}]}]}
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_MultipleChoiceTooManyOptions_Throws()
    {
        var opts = string.Join(",", Enumerable.Range(0, 7).Select(i => $"{{\"text\":\"O{i}\",\"isCorrect\":{(i == 0 ? "true" : "false")}}}"));
        var payload = $$"""
            {"questions":[{"text":"Q?","type":"multipleChoice","options":[{{opts}}]}]}
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_MultipleChoiceBlankOptionText_Throws()
    {
        var payload = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "multipleChoice",
                  "options": [
                    {"text": "", "isCorrect": false},
                    {"text": "B", "isCorrect": true}
                  ]
                }
              ]
            }
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_TrueFalseNonCanonicalLabels_Throws()
    {
        var payload = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "trueFalse",
                  "options": [
                    {"text": "Yes", "isCorrect": true},
                    {"text": "No", "isCorrect": false}
                  ]
                }
              ]
            }
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_TrueFalseNotExactlyTwoOptions_Throws()
    {
        var payload = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "trueFalse",
                  "options": [
                    {"text": "True", "isCorrect": true}
                  ]
                }
              ]
            }
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_TrueFalseBothCorrect_Throws()
    {
        var payload = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "trueFalse",
                  "options": [
                    {"text": "True", "isCorrect": true},
                    {"text": "False", "isCorrect": true}
                  ]
                }
              ]
            }
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_TrueFalseCaseInsensitiveLabels_Accepts()
    {
        var payload = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "trueFalse",
                  "options": [
                    {"text": "true", "isCorrect": true},
                    {"text": "FALSE", "isCorrect": false}
                  ]
                }
              ]
            }
            """;

        var response = AssignmentQuestionResponseParser.Parse(payload);
        response.Questions.Should().HaveCount(1);
        response.Questions[0].Options.Should().HaveCount(2);
    }

    [TestMethod]
    public void Parse_ShortAnswerWithOptions_Throws()
    {
        var payload = """
            {
              "questions": [
                {
                  "text": "Q?",
                  "type": "shortAnswer",
                  "options": [
                    {"text": "A", "isCorrect": true}
                  ]
                }
              ]
            }
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_ShortAnswerWithBlankModelAnswer_Throws()
    {
        var payload = """
            {"questions":[{"text":"Q?","type":"shortAnswer","options":null,"modelAnswer":"   "}]}
            """;

        var act = () => AssignmentQuestionResponseParser.Parse(payload);
        act.Should().Throw<AssignmentQuestionGenerationException>()
            .Which.StatusCode.Should().Be(502);
    }

    [TestMethod]
    public void Parse_ShortAnswerWithModelAnswer_Accepts()
    {
        var payload = """
            {"questions":[{"text":"Q?","type":"shortAnswer","options":null,"modelAnswer":"Glucose"}]}
            """;

        var response = AssignmentQuestionResponseParser.Parse(payload);
        response.Questions.Should().HaveCount(1);
        response.Questions[0].ModelAnswer.Should().Be("Glucose");
    }
}
