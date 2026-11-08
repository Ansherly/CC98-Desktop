// Copyright (c) Bili Copilot. All rights reserved.

namespace Mpv.Core.Enums.Client;

/// <summary>
/// Data format for options and properties. The API functions to get/set
/// properties and options support multiple formats, and this enum describes
/// them.
/// </summary>
public enum MpvFormat
{
    /// <summary>
    /// <para>Invalid. Sometimes used for empty values. This is always defined to 0,</para>
    /// <para>so a normal 0-init of mpv_format (or e.g. mpv_node) is guaranteed to set</para>
    /// <para>this it to Mpv_FORMAT_NONE (which makes some things saner as consequence).</para>
    /// </summary>
    None = 0,

    /// <summary>
    /// <para>The basic type is char*. It returns the raw property string, like</para>
    /// <para>using ${=property} in input.conf (see input.rst).</para>
    /// </summary>
    /// <remarks>
    /// <para>NULL isn't an allowed value.</para>
    /// <para>Warning: although the encoding is usually UTF-8, this is not always the</para>
    /// <para>case. File tags often store strings in some legacy codepage,</para>
    /// <para>and even filenames don't necessarily have to be in UTF-8 (at</para>
    /// <para>least on Linux). If you pass the strings to code that requires</para>
    /// <para>valid UTF-8, you have to sanitize it in some way.</para>
    /// <para>On Windows, filenames are always UTF-8, and libmpv converts</para>
    /// <para>between UTF-8 and UTF-16 when using win32 API functions. See</para>
    /// <para>the &quot;Encoding of filenames&quot; section for details.</para>
    /// <para>Example for reading:</para>
    /// <para>char *result = NULL;</para>
    /// <para>if (mpv_get_property(ctx, &quot;property&quot;, Mpv_FORMAT_STRING,&amp;result)&lt;0)</para>
    /// <para>goto error;</para>
    /// <para>printf(&quot;%s&quot;, result);</para>
    /// <para>mpv_free(result);</para>
    /// <para>Or just use mpv_get_property_string().</para>
    /// <para>Example for writing:</para>
    /// <para>char *value = &quot;the new value&quot;;</para>
    /// <para>// yep, you pass the address to the variable</para>
    /// <para>// (needed for symmetry with other types and mpv_get_property)</para>
    /// <para>mpv_set_property(ctx, &quot;property&quot;, Mpv_FORMAT_STRING,&amp;value);</para>
    /// <para>Or just use mpv_set_property_string().</para>
    /// </remarks>
    String = 1,

    /// <summary>
    /// <para>The basic type is char*. It returns the OSD property string, like</para>
    /// <para>using ${property} in input.conf (see input.rst). In many cases, this</para>
    /// <para>is the same as the raw string, but in other cases it's formatted for</para>
    /// <para>display on OSD. It's intended to be human readable. Do not attempt to</para>
    /// <para>parse these strings.</para>
    /// </summary>
    /// <remarks>Only valid when doing read access. The rest works like Mpv_FORMAT_STRING.</remarks>
    OsdString = 2,

    /// <summary>
    /// <para>The basic type is int. The only allowed values are 0 (&quot;no&quot;)</para>
    /// <para>and 1 (&quot;yes&quot;).</para>
    /// </summary>
    /// <remarks>
    /// <para>Example for reading:</para>
    /// <para>int result;</para>
    /// <para>if (mpv_get_property(ctx, &quot;property&quot;, Mpv_FORMAT_FLAG,&amp;result)&lt;0)</para>
    /// <para>goto error;</para>
    /// <para>printf(&quot;%s&quot;, result ? &quot;true&quot; : &quot;false&quot;);</para>
    /// <para>Example for writing:</para>
    /// <para>int flag = 1;</para>
    /// <para>mpv_set_property(ctx, &quot;property&quot;, Mpv_FORMAT_FLAG,&amp;flag);</para>
    /// </remarks>
    Flag = 3,

    /// <summary>The basic type is int64_t.</summary>
    Int64 = 4,

    /// <summary>The basic type is double.</summary>
    Double = 5,

    /// <summary>The type is mpv_node.</summary>
    /// <remarks>
    /// <para>For reading, you usually would pass a pointer to a stack-allocated</para>
    /// <para>mpv_node value to mpv, and when you're done you call</para>
    /// <para>mpv_free_node_contents(&amp;node).</para>
    /// <para>You're expected not to write to the data - if you have to, copy it</para>
    /// <para>first (which you have to do manually).</para>
    /// <para>For writing, you construct your own mpv_node, and pass a pointer to the</para>
    /// <para>API. The API will never write to your data (and copy it if needed), so</para>
    /// <para>you're free to use any form of allocation or memory management you like.</para>
    /// <para>Warning: when reading, always check the mpv_node.format member. For</para>
    /// <para>example, properties might change their type in future versions</para>
    /// <para>of mpv, or sometimes even during runtime.</para>
    /// <para>Example for reading:</para>
    /// <para>mpv_node result;</para>
    /// <para>if (mpv_get_property(ctx, &quot;property&quot;, Mpv_FORMAT_NODE,&amp;result)&lt;0)</para>
    /// <para>goto error;</para>
    /// <para>printf(&quot;format=%d&quot;, (int)result.format);</para>
    /// <para>mpv_free_node_contents(&amp;result).</para>
    /// <para>Example for writing:</para>
    /// <para>mpv_node value;</para>
    /// <para>value.format = Mpv_FORMAT_STRING;</para>
    /// <para>value.u.string = &quot;hello&quot;;</para>
    /// <para>mpv_set_property(ctx, &quot;property&quot;, Mpv_FORMAT_NODE,&amp;value);</para>
    /// </remarks>
    Node = 6,

    /// <summary>Used with mpv_node only. Can usually not be used directly.</summary>
    NodeArray = 7,

    /// <summary>See Mpv_FORMAT_NODE_ARRAY.</summary>
    NodeMap = 8,

    /// <summary>
    /// <para>A raw, untyped byte array. Only used only with mpv_node, and only in</para>
    /// <para>some very specific situations. (Some commands use it.)</para>
    /// </summary>
    ByteArray = 9,
}
