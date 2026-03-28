Replacing IntPtr with unsafe struct pointers.

# Introduction 

When using .NET Platform Invoke tools to call native C functions in DLLs, it's common for paramaters to contain pointers to structures, classes, or strings. Sometimes it's practical to marshall the data into a managed representation, such as when marshalling a C char* string into a .NET String class.

However, other times the complexity of the native data can be left entirely in the native code. We call these pointers opaque pointers, because from managed code we will never understand the data they point to. Instead our is our responsibility to receive, store, and produce the opaque native pointer when necessary in the API.   

The .NET CTS provides a value type called IntPtr, which can be used to store opaque native pointers. However, it has a serious drawback, in that with respect to the type-system, all IntPtrs are the same type. If your native library has several types of pointers passed across the PInvoke boundary, the compiler can't help you make sure you provided the right IntPtr in the right situation. Providing the wrong pointer to a native C-DLL entry point at best will have eronous results, and at worst will crash your program. 

A safer alternative to IntPtr is the unsafe struct *. Just like IntPtr, unsafe-struct-pointers are a value type used to store opaque pointer data that won't be accessed from managed code.  However, because the structs themselves have types, the compiler can typecheck and assure the proper pointer type is supplied to the proper native entry point.   

Note: Another alternative to IntPtr is the SafeHandle pattern introduced in .NET v2.0 "Whidbey". While this article will focus on the raw use of unsafe struct pointers, we'll also compare this approach to SafeHandle. 

# Pointer Marshalling  

Let's begin by looking at a basic pointer marshalling situation. We will use the .NET wrapper around the Clearsilver HTML templating library as an example. Two functions in the C Clearsilver DLL are:  

```C
NEOERR *hdf_inif(HDF **hdf);
NEOERR *hdf_set_value(HDF *hdf, const char *name, const char *value); 
```

These function calls contain two different pointer types, NEOERR and HDF. Both are pointers to native structures. When using the Clearsilver library from C, those structures are encapsulated opaque data, manipulated only through functions. Our .NET code will treat them the same way. 

One could use PInvoke to provide access to these functions using IntPtr. Our C# .NET imports might look like:  

```C#
[DllImport("libneo", EntryPoint="hdf_init")]
static extern unsafe IntPtr hdf_init(ref IntPtr hdf); 

[DllImport("libneo")] 
static unsafe extern IntPtr hdf_set_value(IntPtr hdf, 
   [MarshalAs(UnmanagedType.LPStr)] string name,
   [MarshalAs(UnmanagedType.LPStr)] string value); 
```

As you can see above, both the NEOERR return value and the HDF paramaters are provided using the type IntPtr. As a result, the compiler can't tell them apart.

When using these paramaters, it is possible to provide one in the place of another without a compiler error. For example, the following code will compile, even though it is invalid IntPtr hdf, neoerr;

```C#
IntPtr hdf, neoerr;

neoerr = hdf_init(ref hdf);
hdf_set_value(neoerr, "foo", "bar");  
```

Instead of typing both of these as IntPtr, we would like the compiler to know these pointers are separate types. The way to do this is via unsafe struct pointers.

Our C# wrapper import code instead becomes: 

```C#
unsafe struct HDF {};
unsafe struct NEOERR {};

[DllImport("libneo", EntryPoint="hdf_init")]
static extern unsafe NEOERR* hdf_init(HDF** hdf); 

[DllImport("libneo")] 
static unsafe extern NEOERR* hdf_set_value(HDF* hdf, 
   [MarshalAs(UnmanagedType.LPStr)] string name, 
   [MarshalAs(UnmanagedType.LPStr)] string value);  
```

The compiler now knows the specific types of the HDF and NEOERR pointers and can tell them apart. A C# managed class can then control access to these unsafe pointers, while still receiving compiler typechecking that the proper pointers are provided at the proper entry points.  

An often raised objection to this method is that the code now requires the `unsafe` keyword. However, keep in mind that there is nothing "safe" about IntPtr and [DllImport]. Either one of which can easily crash the runtime. In fact, if it was up to me, [DLLImport] would be treated as unsafe, IntPtr wouldn't exist,  and unsafe struct pointers would be the advocated mechanism to handle all native pointers.  

Below we've expanded the example to include a constructor and a "safe" setValue() method.  

```C#
  // opaque types
  public unsafe struct HDF {}; 
  public unsafe struct NEOERR {};

  public unsafe class Hdf {
   [DllImport("libneo", EntryPoint="hdf_init")]
  private static extern unsafe NEOERR* hdf_init(HDF **foo);

  // NEOERR* hdf_set_value (HDF *hdf, char *name, char *value)
  [DllImport("libneo")]

  private static unsafe extern NEOERR* hdf_set_value(HDF *hdf,
       [MarshalAs(UnmanagedType.LPStr)] string name,
       [MarshalAs(UnmanagedType.LPStr)] string value);

  // instance data
  internal HDF *hdf_root;

  // constructor 
  public Hdf() {
      fixed (HDF **hdf_ptr = &hdf_root) {
        hdf_init(hdf_ptr);
      }
      // Console.WriteLine((int)hdf_root);
  }

  // managed accessor method 
  public void setValue(string name,string value) {
      NEOERR* err = hdf_set_value(hdf_root,name,value);
  } 
  // ..... more code snipped... 
}  
```

Take special note of the use of the internal [Access Modifier](http://msdn.microsoft.com/en-us/library/ms173121.aspx) on the private unsafe instance pointer HDF *hdf_root.  This assures that only our managed wrapper assembly has permission to access this pointer.  Also take note that we're not yet addressing memory lifetime issues, which we'll briefly cover in the next section.  

# Memory Lifetime 

The main intent of this article is to cover how to replace use of the generic undifferentiated IntPtr with more specifically typed unsafe struct pointers. However, we're going to briefly look at some of the issues related to memory lifetime for these pointers.  

If the code above was used as-is, when the garbage collector released an Hdf instance, the memory pointed to by hdf_root would leak. Further, each call to hdf_init or hdf_set_value potentially leaks a NEOERR structure which should be freed if it exists.   

In the Clearsilver C# wrapper we handle these cases using a combination of [finalization](http://msdn.microsoft.com/en-us/library/0s71x931.aspx) and pointer wrappers. The Hdf class is given a destructor which will free the native pointer sometime after the object is garbage collected.  To simplify this process for the commonly accessed NEOERR type, a separate NeoErr class is created. This class has a static method hNE to handle the NEOERR return value case of "zero if success, object which much be freed if error". That static method looks at the return value, and throws an exception if the return value is non-zero.

Another issue with the above code is that we would like HDF strings to be UTF-8, not ASCII. We'll solve this by using a custom string marashaller which uses UTF-8 instead of ASCII/ANSI strings. You can read more about this technique in my article on [Advanced Topics in PInvoke String Marshaling](http://www.codeproject.com/Articles/138614/Advanced-Topics-in-PInvoke-String-Marshaling).

Below is an expanded form of the Hdf wrapper.

```C#
public unsafe class Hdf {

    [DllImport("libneo", EntryPoint = "hdf_init")]
    private static extern unsafe NEOERR* hdf_init(HDF** foo);

    // NEOERR* hdf_set_value (HDF *hdf, char *name, char *value)
    [DllImport("libneo")]
    private static unsafe extern NEOERR* hdf_set_value(HDF* hdf,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(UTF8Marshaler))] string name,
            [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(UTF8Marshaler))] string value);

    internal unsafe HDF* hdf_root;

    public Hdf() {
        fixed (HDF** hdf_ptr = &hdf_root) {
            hdf_init(hdf_ptr);
        }
    }

    ~Hdf() {  // destructor/finalizer
        if (hdf_root != null) {
           fixed (HDF** phdf = &hdf_root) { 
               // free the native pointer after this object is collected
               hdf_destroy(phdf);  
           }
        }
    }
    public void setValue(string name, string value) {
        NeoError.hNE(hdf_set_value(hdf_root, name, value));
    }

    // .. more stub methods...

}
```

To see the details of the NeoErr class, the full Hdf and Cs wrappers,  or more examples of how to use unsafe structs for safer PInvoke entrypoints, check out the full [Clearsilver C# wrapper](http://dj1.willowmail.com/~jeske/Projects/ClearsilverCsharp/), and the clearsilver source kit available from [clearsilver.net](http://www.clearsilver.net/).    

# Points of Interest  

The above unsafe struct pointer usage is valid according to the Common Type System spec, and works in Microsoft.NET. However, several versions of the Mono .NET runtime marshalling code did not properly handle unsafe struct pointers in PInvoke entry points.  Therefore, you'll need to be using the very latest Mono 2.12 (or very very old versions of Mono) for this to work.  

Another model for marshalling pointers without the type danger of IntPtr is to use SafeHandle. Like unsafe struct pointers, SafeHandles replace IntPtr in [DllImport] entrypoints, allowing strongly typed native pointer handling. unsafe struct provides a familiar coding idiom for C/C++ programmers, allowing the use of try/finally, finalizers, and situations such as the double-indirect pointers required in the above hdf_init(HDF **hdf) call. SafeHandle, on the other hand, provides a solution to a tricky [GC finalizer race condition](http://blogs.msdn.com/b/bclteam/archive/2005/03/16/396900.aspx) which Platform Invoke code can fall victim to.   

# History  

* 2012 August - Initial release  

# License

This article and included code is released under Apache 2.0.