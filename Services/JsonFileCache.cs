using CC98.Services.Extensions;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CC98.Objects;
using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Memory;
using Polly;

namespace CC98.Services;

/// <summary>
///     用于缓存大型 JSON 数据。
/// </summary>
public class JsonFileCache<T>
where T : class
{
    // 私有构造函数，仅内部使用
    /// <summary>
    /// 初始化 <see cref="JsonFileCache{T}"/> 对象的新实例。
    /// </summary>
    /// <param name="filePath">缓存对应的文件路径。</param>
    public JsonFileCache(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("路径不能为空或空白", nameof(filePath));

        FilePath = filePath;
    }

    /// <summary>
    /// 获取缓存对象关联的文件路径。
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 内存缓存使用的键对象。
    /// </summary>
    private string MemoryCacheKey => $"JsonFileCache_{FilePath}";

    /// <summary>
    /// 配置缓存项设置的方法。
    /// </summary>
    /// <param name="entry">要配置缓存项目。</param>
    private static void ConfigureCacheEntry(ICacheEntry entry)
    {
        entry.SetOptions(CreateEntryOptions());
    }

    /// <summary>
    /// 构建缓存条目选项。内存缓存只是磁盘文件的"热副本":滑动过期后按需从磁盘重新反序列化即可。
    /// 之前 NeverRemove + 永不过期会让大对象图(版面树/首页数据等)在进程内永驻。
    /// </summary>
    private static MemoryCacheEntryOptions CreateEntryOptions() => new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(10),
        Priority = CacheItemPriority.Normal,
    };

    /// <summary>
    /// 尝试加载缓存的数据。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>表示异步操作的任务。操作结果包含缓存或新加载的数据。如果未获得缓存的数据，则返回 <see langword="null"/>。</returns>
    /// <exception cref="InvalidOperationException">当缓存加载失败。</exception>
    public Task<T?> GetDataAsync(CancellationToken cancellationToken = default)
    {
        return App.Current.MemoryCache.GetOrCreateAsync(MemoryCacheKey, async entry =>
        {
            ConfigureCacheEntry(entry);
            return await TryLoadDataFromFileAsync(cancellationToken);
        });
    }

    /// <summary>
    /// 立即更新缓存并写入文件。
    /// </summary>
    /// <param name="data">新数据。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task UpdateDataAsync(T data, CancellationToken cancellationToken = default)
    {
        // 强制改写内存缓存(应用与读取一致的过期策略,否则 Set 路径会绕开过期配置)
        App.Current.MemoryCache.Set(MemoryCacheKey, data, CreateEntryOptions());

        // 尝试保存数据
        await WriteRetryExecutor.ExecuteAsync(async ct =>
        {
            await using var file = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(file, data, CC98JsonContext.Default.GetTypeInfo<T>(), ct);
        }, cancellationToken);
    }

    /// <summary>
    /// 放弃本地文件缓存，下次访问将立即重新加载。
    /// </summary>
    public void Discard()
    {
        App.Current.MemoryCache.Remove(MemoryCacheKey);
    }

    /// <summary>
    /// 强制删除缓存文件。
    /// </summary>
    public void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Trace.TraceError("删除 JSON 缓存文件时发生错误, 文件 = {0}, 错误 = {1}", FilePath, ex.Message);
        }
        finally
        {
            // 立即放弃缓存
            Discard();
        }
    }

    /// <summary>
    /// 从文件中加载数据的核心方法。首次缓存时调用。
    /// </summary>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>表示异步操作的任务。操作结果为文件中包含的数据，如果无法加载数据，则返回 <see langword="null"/>。</returns>
    private async Task<T?> TryLoadDataFromFileAsync(CancellationToken cancellationToken = default)
    {

        try
        {
            await using var file = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync(file, CC98JsonContext.Default.GetTypeInfo<T>(), cancellationToken);
        }
        // 发生错误，记录异常并返回空
        catch (Exception ex)
        {
            Trace.TraceError("从文件中加载 JSON 缓存对象失败，文件 = {0}, 错误 = {1}", FilePath, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 用于写入文件的带错误重试执行的管线对象。
    /// </summary>
    // ReSharper disable once StaticMemberInGenericType
    private static ResiliencePipeline WriteRetryExecutor { get; } = new ResiliencePipelineBuilder()
        .AddTimeout(TimeSpan.FromMinutes(1)) // 每次最多允许一分钟
        .AddRetry(new()
        {
            MaxRetryAttempts = 3, // 尝试 3 次
            Delay = TimeSpan.FromSeconds(10), // 重试间隔 10s
            BackoffType = DelayBackoffType.Linear, // 线性增加间隔时间

            // 允许重试的错误类型
            ShouldHandle = new PredicateBuilder()
                .Handle<IOException>()
                .Handle<UnauthorizedAccessException>(),

            // 重试日志记录
            OnRetry = obj =>
            {
                Trace.TraceWarning("第 {0} 次尝试写入文件失败，原因 = {1}, 等待 {2:g} 后重试。", obj.AttemptNumber + 1,
                    obj.Outcome.Exception?.Message, obj.RetryDelay);
                return default;
            }
        }).Build();
}