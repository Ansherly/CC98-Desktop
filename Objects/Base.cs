using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;

/// <summary>
/// 存放基础对象模型
/// </summary> 

namespace CC98.Objects;

public class ApiResponse<T>
{
    [MemberNotNullWhen(true, nameof(Data))]
    public bool IsSuccess { get; set; }
    
    public T? Data { get; set; }
    public string Message { get; set; } = string.Empty;
    public int StatusCode { get; set; }

    public bool IsNotValid => !IsSuccess || Data == null;

    public static ApiResponse<T> Success(T data, string message = "")
    {
        return new()
        {
            IsSuccess = true,
            Data = data,
            Message = message,
            StatusCode = (int)HttpStatusCode.OK
        };
    }

    public static ApiResponse<T> Fail(string message, int statusCode = 0)
    {
        return new()
        {
            IsSuccess = false,
            Message = message,
            StatusCode = statusCode
        };
    }
}

public class ApiResponse
{
    public bool IsSuccess { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int StatusCode { get; set; }

    public static ApiResponse Success(string content, string message = "")
    {
        return new()
        {
            IsSuccess = true,
            Content = content,
            Message = message,
            StatusCode = (int)HttpStatusCode.OK
        };
    }

    public static ApiResponse Fail(string message, int statusCode = 0)
    {
        return new()
        {
            IsSuccess = false,
            Message = message,
            StatusCode = statusCode
        };
    }
}

/// <summary>
///     增量更新模型
/// </summary>
public class Increment(int pageSize = 10, int currentPage = 0, bool hasMore = false)
{
    public int CurrentPage = currentPage;

    //应假定没有更多项，由返回项项数决定是否还有更多
    public bool HasMore = hasMore;
    public int PageSize = pageSize;

    public int StartIndex => CurrentPage * PageSize;

    public void Clear()
    {
        CurrentPage = 0;
        HasMore = false;
    }

    public async Task LoadMore(int currentIndex, Func<Task<bool>> load)
    {
        // 防重入:增量加载期间忽略重复触发(ItemsRepeater 回收/重复 Prepare 会重复调用)
        if (_loading) return;
        if (HasMore && (currentIndex + 1) % PageSize == 0)
        {
            _loading = true;
            CurrentPage++;
            try
            {
                var success = await load();
                //如果没有成功，则回退页码，等待下一次尝试
                if (!success) CurrentPage--;
            }
            finally
            {
                _loading = false;
            }
        }
    }

    //强制加载下一页
    public async Task LoadNextPage(Func<Task<bool>> load)
    {
        // 注意:必须先判条件再置防重入标志 —— 提前 return 时不会走 finally,
        // 否则 _loading 会永久停在 true,该分页器此后再也无法增量加载。
        if (_loading) return;
        if (!HasMore) return;
        _loading = true;
        CurrentPage++;
        try
        {
            var success = await load();
            //如果没有成功，则回退页码，等待下一次尝试
            if (!success) CurrentPage--;
        }
        finally
        {
            _loading = false;
        }
    }

    public async Task LoadLastPage(Func<Task<bool>> load)
    {
        // 同上:条件判断务必在置位之前
        if (_loading) return;
        if (CurrentPage <= 0) return;
        _loading = true;
        CurrentPage--;
        try
        {
            var success = await load();
            //如果没有成功，则回退页码，等待下一次尝试
            if (!success) CurrentPage++;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    ///     跳转到指定页(0-based)并加载。翻页是"整页替换"语义。
    ///     与 <see cref="LoadNextPage" /> / <see cref="LoadLastPage" /> 的区别:
    ///       1. 目标页由调用方给定,不依赖 <see cref="HasMore" /> —— 分页组件可以跳回已经
    ///          加载过的页,那时 HasMore 可能早就被置为 false 了;
    ///       2. 目标页恰好等于当前页时也照常加载(即"重新载入当前页")。
    /// </summary>
    /// <returns>
    ///     true 表示加载成功;false 表示被防重入挡下、页码非法,或加载失败(此时页码已回退,
    ///     调用方应把分页组件同步回 <see cref="CurrentPage" />)。
    /// </returns>
    public async Task<bool> LoadPage(int targetIndex, Func<Task<bool>> load)
    {
        // 条件判断务必在置位之前:提前 return 时不会走 finally,
        // 否则 _loading 会永久停在 true,该分页器此后再也无法加载。
        if (_loading) return false;
        if (targetIndex < 0) return false;

        var previousPage = CurrentPage;
        _loading = true;
        CurrentPage = targetIndex;
        try
        {
            var success = await load();
            // 失败则回退页码,让 CurrentPage 始终与"当前展示的内容"一致
            if (!success) CurrentPage = previousPage;
            return success;
        }
        finally
        {
            _loading = false;
        }
    }

    private bool _loading;
}