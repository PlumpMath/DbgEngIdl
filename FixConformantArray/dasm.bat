
call VsDevCmd.bat

midl DbgEng.idl

tlbimp DbgEng.tlb /namespace:Interop.DbgEng /out:_Interop.DbgEng.dll

ildasm _Interop.DbgEng.dll /out:_Interop.DbgEng.il
