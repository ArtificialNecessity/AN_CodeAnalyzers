# Advanced Topics in PInvoke String Marshaling

**David Jeske** — December 21, 2010

*An exploration of some subtle different ways that strings can be marshaled with PInvoke.*

*Originally published on CodeProject.com (now defunct). Preserved from archive.org.*

---

## Introduction

The .NET Platform Invoke tools, used through the `DllImportAttribute`, are a powerful and simple mechanism to interface with unmanaged DLLs. However, there are many subtleties that are important when addressing string buffer ownership responsibility with unmanaged code. This article covers some of the additional options besides the default `MarshalAs(UnmanagedType.LPStr)`.

## Background

So you've bought into .NET hook line and sinker, but you still have a bunch of pre-existing native code DLLs around you want to make use of. Platform Invoke provides a mechanism to wrap those native DLLs, and to easily marshal some common types of parameters, however, this does not cover all cases. Notably, some string marshalling to native code is complex and must be handled using different techniques.

- What if the native code expects to own the string after the call?
- What if you'd like to marshal UTF8 strings instead of ANSI/ASCII strings?
- What if the parameter is really an out-buffer that the target is going to write to?

These are just a few of the realities that exist when interfacing with native-code DLLs. While you'll learn that it's easy to handle any of these situations using PInvoke, they are each distinctly different and require different code.

## Using the Code

We're not going to cover the basic concepts of PInvoke here. For that, we recommend you review one of the many excellent tutorials available elsewhere. Instead, we're going to consider some of the different ways one can use PInvoke to interact with a native-code DLL entry point declared as:

```c
void my_function(char *data);
```

There are several possible contracts this C-code could have with us over the character pointer `data`. Below are a few of those contracts. In all cases, we assume a null-terminated ANSI/ASCII string.

1. `data` may be read-only during the lifetime of the call, and never stored by native code
2. `data` may be modified during the lifetime of the call, and never stored by native code
3. `data` may be adopted by native code as its own, where native code expects to free it later

If you're familiar with PInvoke tutorials, you should be familiar with how to handle case #1 above. We simply declare the entry point and specify the built-in LPStr marshaler.

```csharp
[DllImport("mydllname")]
extern static unsafe void my_function(
    [MarshalAs(UnmanagedType.LPStr)] string data);
```

The attribute decorator is simple, and automatically handles case #1 above. Before the call, the built-in marshaler allocates a fixed-location buffer for a null-terminated string, and copies an ANSI/ASCII compatible version of the managed string into the buffer. After the call, the marshaler automatically frees the buffer, making sure not to leak memory.

## Case #2: Mutable Buffer

How do we then handle #2 above, where the data may be modified during the call? The default marshaler doesn't consider the contents of the buffer after the call, so how could we see the modification? A quick read over the documentation shows us that there is an `[Out]` attribute we can stick on the marshaler, but the true challenge lies underneath the covers.

If the native call is simply going to modify a few of the characters, while leaving the length the same, then we merely need to copy the contents back into managed code afterwards. However, if it's going to tack more data onto the end of the string, our buffer isn't big enough for any more data! It'll be writing over random other things in memory.

How could the native code know how big our buffer is? Such is the complexity of unmanaged code interfaces. Perhaps the function would take an additional parameter, the `max_length` of the string buffer, so it can be careful not to overwrite the end of the buffer. Perhaps the contract just expects that the maximum data that will ever be written onto the string buffer is known to be 4000 characters. While these contracts might seem messy, such is the reality of unmanaged interfaces. We'll assume the latter scenario.

Our unmanaged contract is now:

```c
void my_function(char *data);
// data should point to a char[4000] that we can write to during the call
```

In order to satisfy this call, we need managed code to allocate a 4000 character buffer, and then copy the contents out afterwards. If we know the string is an ANSI string, the following code will achieve the desired result:

```csharp
private class imp {
    [DllImport("mydllname")]
    private extern static unsafe my_function(IntPtr data);
}

public unsafe void my_function(out string data) {
    IntPtr buffer = (IntPtr)stackalloc byte[4000];

    imp.my_function(buffer);
    data = Marshal.PtrToStringAnsi(buffer);
}
```

`stackalloc` makes room for the allocation on the stack, where it's guaranteed to remain fixed and available for the lifetime of the call. `Marshal.PtrToStringAnsi()` automatically converts a null-terminated ANSI/ASCII character buffer into a managed string for us, allocating the managed string in the process.

If the string parameter needs to be passed both into and out of the function, a new marshaling stub can handle that case as well.

```csharp
private class imp {
    [DllImport("mydllname")]
    private extern static unsafe my_function(IntPtr data);
}

public unsafe void my_function(ref string data) {
    // allocate room on the stack
    IntPtr buffer = (IntPtr)stackalloc byte[4000];
    // convert the managed string into an ASCII byte[]
    byte[] data_buf = Encoding.ASCII.GetBytes(data);
    // check for out-of-bounds
    if (data_buf.Length > (4000 - 1)) {
        throw new Exception("input too large for fixed size buffer");
    }
    // .. then copy the bytes
    Marshal.Copy(data_buf, 0, buffer, data_buf.Length);
    Marshal.WriteByte(buffer + data_buf.Length, 0); // terminating null

    imp.my_function(buffer);
    // after the call, marshal the bytes back out
    data = Marshal.PtrToStringAnsi(buffer);
}
```

## Case #3: Ownership Transfer with UTF8

However, what now of case #3? We can't allow native code to take ownership of a data structure that's on the stack, because it won't exist after the call! We'll need to allocate memory that it can take ownership of. Further, what if the string we're sending is a UTF8 string, not ANSI/ASCII? There isn't an automatic Marshal converter for UTF8 strings, so we'll need to do a bit more work. Let's consider the following native call:

```c
void my_function(char *data);
// data points to a heap allocated UTF8 string which will be adopted by
// the my_function DLL. It will be freed later by the DLL when it's no
// longer needed.
```

This case introduces some more complexity, as a typical system has many allocators. The first important task is to figure out which allocator the native DLL is using, because it's important that we allocate memory using the same allocator, if the native code is going to free it. If you have control over the build of the native DLL, one safe possibility is to export an explicit `my_malloc` and `my_free` from the native DLL which uses the same allocator it uses, assuring your .NET runtime can use those entry points to always access the same allocators. However, if you know the DLL uses the standard global Windows allocator, then you may use `Marshal.AllocHGlobal` and `Marshal.FreeHGlobal`.

```csharp
private class imp {
    [DllImport("mydllname")]
    private extern static unsafe my_function(IntPtr data);
}

public unsafe void my_function(string data) {
    IntPtr buffer = IntPtr.Zero;

    try {
        // remember the byte[] is not null terminated
        byte[] strbuf = Encoding.UTF8.GetBytes(data);

        // .. so add one more byte for the null termination
        buffer = Marshal.AllocHGlobal(strbuf.Length + 1);

        // .. then copy the bytes
        Marshal.Copy(strbuf, 0, buffer, strbuf.Length);
        Marshal.WriteByte(buffer + strbuf.Length, 0); // terminating null
    } catch (Exception e) {
        // be sure to free the buffer if it was allocated
        if (buffer != IntPtr.Zero) {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // call the function with our buffer
    imp.my_function(buffer);
}
```

That was quite a bit more code! However, if you follow it section by section, you'll see that it meets the native code contract. It converts our managed string into a UTF8 `byte[]`. Then it allocates a native code buffer big enough for this array plus a null-termination at the end. It copies the bytes from the array, and then writes a null character at the end position of the native buffer.

I'm going to raise the issues of allocators again, because it's critically important. If the native code uses an allocator which is not the same one used by `AllocHGlobal`, then when it tries to free this memory, bad things will happen. If you're lucky, the program will crash when you test it. If you're not, memory will silently be corrupted. Another allocator we have access to under .NET is `CoTaskMemAlloc`. However, the safest route for any DLL which expects memory ownership to cross boundaries like this, is for it to explicitly provide its own `my_alloc` and `my_free` entry-points. In this case, you would use those entry-points to allocate and free the buffer above, in place of `AllocHGlobal` and `FreeHGlobal`.

## Building a Custom Marshaler

I know what you're thinking, because I was thinking it too. How can I use the simple decorator syntax for my own marshaler, instead of all this wrapper code? Fortunately, Platform Invoke provides a way to do just that, by authoring a custom marshaling class. Here is an example of a custom marshaling class which acts exactly like the LPStr marshaler, except that it converts into UTF8 instead of ANSI strings:

```csharp
public class UTF8Marshaler : ICustomMarshaler {
    static UTF8Marshaler static_instance;

    public IntPtr MarshalManagedToNative(object managedObj) {
        if (managedObj == null)
            return IntPtr.Zero;
        if (!(managedObj is string))
            throw new MarshalDirectiveException(
                "UTF8Marshaler must be used on a string.");

        // not null terminated
        byte[] strbuf = Encoding.UTF8.GetBytes((string)managedObj);
        IntPtr buffer = Marshal.AllocHGlobal(strbuf.Length + 1);
        Marshal.Copy(strbuf, 0, buffer, strbuf.Length);

        // write the terminating null
        Marshal.WriteByte(buffer + strbuf.Length, 0);
        return buffer;
    }

    public unsafe object MarshalNativeToManaged(IntPtr pNativeData) {
        byte* walk = (byte*)pNativeData;

        // find the end of the string
        while (*walk != 0) {
            walk++;
        }
        int length = (int)(walk - (byte*)pNativeData);

        // should not be null terminated
        byte[] strbuf = new byte[length];
        // skip the trailing null
        Marshal.Copy((IntPtr)pNativeData, strbuf, 0, length);
        string data = Encoding.UTF8.GetString(strbuf);
        return data;
    }

    public void CleanUpNativeData(IntPtr pNativeData) {
        Marshal.FreeHGlobal(pNativeData);
    }

    public void CleanUpManagedData(object managedObj) {
    }

    public int GetNativeDataSize() {
        return -1;
    }

    public static ICustomMarshaler GetInstance(string cookie) {
        if (static_instance == null) {
            return static_instance = new UTF8Marshaler();
        }
        return static_instance;
    }
}
```

Using this custom marshaler is as easy as the simple built-in LPStr marshaler. For our entry-point, the decorator looks like this:

```csharp
[DllImport("mydllname")]
extern static void my_function(
    [MarshalAs(UnmanagedType.CustomMarshaler,
        MarshalTypeRef = typeof(UTF8Marshaler))]
    string data);
```

## Special Considerations

Custom marshaler instances are retrieved using the `GetInstance()` method, to allow the common optimization that the marshaler is entirely static and doesn't need to be reallocated new for each argument. If you wish to maintain state for a particular marshaling instance, it's important to return a new instance of your class from `GetInstance()`.

The above code always allocates the string buffer into the heap, but it may be advantageous to allocate it into the stack for strings you know will be reasonable size. One way to do this would be to make a separate custom marshaler that always uses the stack, naming it something obvious such as `UTF8StackMarshaller`. Another possibility is to return a real-instance from get-instance, and then store state inside the instance that knows whether the data was marshaled to the stack or the heap, probably based on the size of the string.

---

*Originally published December 21, 2010 on CodeProject.com.*
*Copyright David Jeske. Licensed under CPOL.*