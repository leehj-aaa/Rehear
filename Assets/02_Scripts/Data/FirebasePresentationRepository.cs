using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Extensions;
using Rehear.Evc.Data;

public sealed class FirebasePresentationRepository : IPresentationRepository
{
    private const string DatabaseRootPath = "presentation_data";

    public Task<PresentationLoadResult> LoadAsync(string pin, CancellationToken cancellationToken)
    {
        if (!PresentationDataValidator.IsFourDigitPin(pin))
            return Task.FromResult(PresentationLoadResult.Error(PresentationLoadStatus.Invalid, "PIN 번호 4자리를 모두 입력해주세요."));

        var completion = new TaskCompletionSource<PresentationLoadResult>();
        var registration = cancellationToken.Register(() => completion.TrySetResult(
            PresentationLoadResult.Error(PresentationLoadStatus.Cancelled, "세션 불러오기가 취소되었습니다.")));

        FirebaseDatabase.DefaultInstance.GetReference(DatabaseRootPath).Child(pin).GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                registration.Dispose();
                if (completion.Task.IsCompleted) return;
                if (task.IsCanceled)
                {
                    completion.TrySetResult(PresentationLoadResult.Error(PresentationLoadStatus.Cancelled, "세션 불러오기가 취소되었습니다."));
                    return;
                }
                if (task.IsFaulted)
                {
                    completion.TrySetResult(PresentationLoadResult.Error(
                        PresentationLoadStatus.Failed,
                        "서버 연결에 실패했습니다.",
                        new[] { "Firebase GetValueAsync failed." }));
                    return;
                }

                var snapshot = task.Result;
                if (snapshot == null || !snapshot.Exists)
                {
                    completion.TrySetResult(PresentationLoadResult.Error(PresentationLoadStatus.NotFound, "존재하지 않는 PIN 번호입니다."));
                    return;
                }

                var data = Map(snapshot, pin, out var mappingErrors);
                var validation = PresentationDataValidator.Validate(data);
                if (!validation.IsValid)
                {
                    var diagnostics = new List<string>(mappingErrors);
                    diagnostics.AddRange(validation.Errors);
                    completion.TrySetResult(PresentationLoadResult.Error(
                        PresentationLoadStatus.Invalid,
                        "세션 정보가 올바르지 않습니다. 관리자에게 문의해주세요.",
                        diagnostics));
                    return;
                }

                completion.TrySetResult(PresentationLoadResult.Success(data));
            });
        return completion.Task;
    }

    private static PresentationDataDto Map(DataSnapshot root, string pin, out IReadOnlyList<string> errors)
    {
        if (!(root?.Value is IDictionary values))
        {
            errors = new[] { "Firebase presentation root is not an object." };
            return null;
        }

        return FirebasePresentationValueMapper.Map(values, pin, out errors);
    }
}
