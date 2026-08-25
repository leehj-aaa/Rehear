using System;
using System.Collections.Generic;
using Rehear.Evc.Contracts;
using TMPro;
using UnityEngine;

namespace Rehear.Evc.UI
{
    public enum QuestionPresenterState
    {
        Idle,
        Generating,
        Ready,
        Failed
    }

    public sealed class QuestionPresenter : MonoBehaviour
    {
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private GameObject retryObject;

        private IReadOnlyList<GeneratedQuestion> questions = Array.Empty<GeneratedQuestion>();
        private int currentIndex;

        public QuestionPresenterState State { get; private set; } = QuestionPresenterState.Idle;
        public int CurrentIndex => currentIndex;
        public int Count => questions.Count;

        public void ShowGenerating()
        {
            State = QuestionPresenterState.Generating;
            questions = Array.Empty<GeneratedQuestion>();
            currentIndex = 0;
            SetStatus("질문을 생성하고 있습니다.");
            if (questionText != null) questionText.text = string.Empty;
            if (retryObject != null) retryObject.SetActive(false);
        }

        public void ShowReady(IReadOnlyList<GeneratedQuestion> generatedQuestions)
        {
            questions = generatedQuestions ?? Array.Empty<GeneratedQuestion>();
            currentIndex = 0;
            State = questions.Count > 0 ? QuestionPresenterState.Ready : QuestionPresenterState.Failed;
            if (retryObject != null) retryObject.SetActive(State == QuestionPresenterState.Failed);
            RefreshQuestion();
        }

        public void ShowFailed(string userMessage)
        {
            State = QuestionPresenterState.Failed;
            SetStatus(string.IsNullOrWhiteSpace(userMessage) ? "질문을 불러오지 못했습니다." : userMessage);
            if (questionText != null) questionText.text = string.Empty;
            if (retryObject != null) retryObject.SetActive(true);
        }

        public bool MoveNext()
        {
            if (State != QuestionPresenterState.Ready || currentIndex >= questions.Count - 1)
                return false;
            currentIndex++;
            RefreshQuestion();
            return true;
        }

        private void RefreshQuestion()
        {
            if (questions.Count == 0)
            {
                SetStatus("표시할 질문이 없습니다.");
                if (questionText != null) questionText.text = string.Empty;
                return;
            }

            SetStatus((currentIndex + 1) + " / " + questions.Count);
            if (questionText != null)
                questionText.text = questions[currentIndex].question;
        }

        private void SetStatus(string value)
        {
            if (statusText != null)
                statusText.text = value;
        }
    }
}
