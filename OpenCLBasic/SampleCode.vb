Option Strict Off
Imports System.Runtime.CompilerServices
Imports System.Runtime.InteropServices
Imports OpenCLBasic.FakeRuntime

Public Module SampleProgram

    Const sampler As Sampler = CLK.NORMALIZED_COORDS_FALSE Or CLK.ADDRESS_CLAMP Or CLK.FILTER_NEAREST

    ' �Ƚ� BGRA32 ���ظ�ʽ��ͼƬ A �� B�������ɫ��һ��, ������� A ��ɫ�����ص㡣�������͸�����ص㡣
    ' Public -> __kernel, ByVal -> __read_only, <Out> ByRef -> __write_only, GetGlobalId ���� -> const
    ' ���е�����Ӧ�ñ���ֱֹ��ִ�С�
    <MethodImpl(MethodImplOptions.ForwardRef)>
    Public Sub ImageDiffBlend(ByVal imageA As Image2D, ByVal imageB As Image2D, <Out> ByRef imageC As Image2D)
        Dim x As Integer = GetGlobalId(0)
        Dim y As Integer = GetGlobalId(1)

        Dim coord As Int2 = (x, y)

        Dim A As UInt4 = ReadImageUI(imageA, sampler, coord)
        Dim B As UInt4 = ReadImageUI(imageB, sampler, coord)
        Dim C As UInt4 = 0

        If A <> B Then
            C = -A
            C.w = 255
        End If

        WriteImageUI(imageC, coord, A - B)
    End Sub
End Module
