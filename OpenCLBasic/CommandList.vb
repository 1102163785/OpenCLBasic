﻿Imports System.Drawing
Imports System.Runtime.InteropServices
Imports OpenCL.Net

Public Class CommandList
    Implements IOpenCLResourceCreatorWithContext, IDisposable

    Friend Sub New(method As OpenCLMethod, Optional commandQueueProperties As CommandQueueProperties = CommandQueueProperties.None)
        _method = method
        _cmdQueue = CreateCommandQueue(DeviceContext, Device, commandQueueProperties)
    End Sub

    Shared s_intPtrSize As New IntPtr(Marshal.SizeOf(GetType(IntPtr)))

    Private _pStack As UInteger = 0
    Private _maxWidth, _maxHeight As Integer
    Private _memLocFlags As MemFlags = MemFlags.UseHostPtr

    Private ReadOnly _copyBackMem As New Stack(Of IMem)
    Private ReadOnly _disposeMem As New List(Of IMem)
    Private ReadOnly _copyBackPtr As New Stack(Of IntPtr)
    Private ReadOnly _method As OpenCLMethod
    Private ReadOnly _cmdQueue As CommandQueue
    Private ReadOnly clImageFormat As New ImageFormat(ChannelOrder.RGBA, ChannelType.Unsigned_Int8)

    Public ReadOnly Property Device As Device Implements IOpenCLResourceCreator.Device
        Get
            Return _method.Device
        End Get
    End Property

    Public ReadOnly Property DeviceContext As Context Implements IOpenCLResourceCreatorWithContext.DeviceContext
        Get
            Return _method.DeviceContext
        End Get
    End Property

    ''' <summary>
    ''' 表示是否使用显存。默认是不使用。需要在显卡进行复杂计算的时候建议将此选项设置为 True。
    ''' </summary>
    Public Property UseGpuMemory As Boolean
        Get
            Return _memLocFlags = MemFlags.CopyHostPtr
        End Get
        Set(value As Boolean)
            _memLocFlags = If(value, MemFlags.CopyHostPtr, MemFlags.UseHostPtr)
        End Set
    End Property

    ''' <summary>
    ''' 将用于输入的位图的参数加入参数栈中, 并将图片写入的命令加入命令队列。
    ''' </summary>
    Public Sub LoadInputImage2D(imageSize As Rectangle, backBuffer As IntPtr)
        Dim mem = CreateImage2D(_method.DeviceContext, _memLocFlags Or MemFlags.ReadOnly,
                  clImageFormat, New IntPtr(imageSize.Width),
                  New IntPtr(imageSize.Height), IntPtr.Zero,
                  backBuffer)

        SetKernelArg(_method.Kernel, _pStack, s_intPtrSize, mem)
        _pStack += 1UI

        'Copy input image from the host to the GPU.  
        Dim originPtr = {IntPtr.Zero, IntPtr.Zero, IntPtr.Zero}
        Dim regionPtr = {New IntPtr(imageSize.Width), New IntPtr(imageSize.Height), New IntPtr(1)}
        Dim workGroupSizePtr = regionPtr

        EnqueueWriteImage(_cmdQueue, mem, True, originPtr, regionPtr, IntPtr.Zero,
                          IntPtr.Zero, backBuffer, 0, Nothing, Nothing)

        _maxWidth = Math.Max(_maxWidth, imageSize.Width)
        _maxHeight = Math.Max(_maxHeight, imageSize.Height)

        _disposeMem.Add(mem)
    End Sub

    ''' <summary>
    ''' 将用于输出的位图的参数加入参数栈中。
    ''' </summary>
    Public Sub LoadOutputImage2D(imageSize As Rectangle, backBuffer As IntPtr)
        Dim mem = CreateImage2D(_method.DeviceContext, _memLocFlags Or MemFlags.WriteOnly,
                  clImageFormat, New IntPtr(imageSize.Width),
                  New IntPtr(imageSize.Height), IntPtr.Zero,
                  backBuffer)

        SetKernelArg(_method.Kernel, _pStack, s_intPtrSize, mem)
        _pStack += 1UI

        _maxWidth = Math.Max(_maxWidth, imageSize.Width)
        _maxHeight = Math.Max(_maxHeight, imageSize.Height)

        _copyBackMem.Push(mem)
        _copyBackPtr.Push(backBuffer)

        _disposeMem.Add(mem)
    End Sub

    Public Sub LoadInput(ptr As IntPtr, length As Integer)
        Dim mem = CreateBuffer(_method.DeviceContext, _memLocFlags Or MemFlags.ReadOnly,
                               New IntPtr(length), ptr)

        SetKernelArg(_method.Kernel, _pStack, s_intPtrSize, mem)
        _pStack += 1UI

        _disposeMem.Add(mem)
    End Sub

    Public Sub LoadOutput(ptr As IntPtr, length As Integer)
        Dim mem = CreateBuffer(_method.DeviceContext, _memLocFlags Or MemFlags.WriteOnly,
                               New IntPtr(length), ptr)

        SetKernelArg(_method.Kernel, _pStack, s_intPtrSize, mem)
        _pStack += 1UI

        _disposeMem.Add(mem)
    End Sub

    ''' <summary>
    ''' 以处理一组数据的方式调用 OpenCL 方法。
    ''' </summary>
    Public Sub [Call]()
        Dim regionPtr = {New IntPtr(_maxWidth), New IntPtr(_maxHeight), New IntPtr(1)}
        Dim workGroupSizePtr = regionPtr
        EnqueueNDRangeKernel(_cmdQueue, _method.Kernel, 2, Nothing, workGroupSizePtr, Nothing, 0, Nothing, Nothing)
        Finish(_cmdQueue)

        For i = 0 To _disposeMem.Count - 1
            ReleaseMemObject(_disposeMem(i))
        Next
        _disposeMem.Clear()

        _maxHeight = 0
        _maxWidth = 0
    End Sub

    ''' <summary>
    ''' 以处理图片的方式调用 OpenCL 方法。
    ''' </summary>
    Public Sub CallImage2D()
        Dim originPtr = {IntPtr.Zero, IntPtr.Zero, IntPtr.Zero}
        Dim regionPtr = {New IntPtr(_maxWidth), New IntPtr(_maxHeight), New IntPtr(1)}
        Dim workGroupSizePtr = regionPtr
        EnqueueNDRangeKernel(_cmdQueue, _method.Kernel, 2, Nothing, workGroupSizePtr, Nothing, 0, Nothing, Nothing)
        Finish(_cmdQueue)

        Do While _copyBackMem.Count > 0
            EnqueueReadImage(_cmdQueue, _copyBackMem.Pop, True, originPtr, regionPtr,
                             IntPtr.Zero, IntPtr.Zero, _copyBackPtr.Pop, 0, Nothing, Nothing)
        Loop

        For i = 0 To _disposeMem.Count - 1
            ReleaseMemObject(_disposeMem(i))
        Next
        _disposeMem.Clear()

        _maxHeight = 0
        _maxWidth = 0
    End Sub

#Region "IDisposable Support"
    Private disposedValue As Boolean ' 要检测冗余调用

    ' IDisposable
    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                ' TODO: 释放托管状态(托管对象)。
                GC.SuppressFinalize(Me)
            End If

            _cmdQueue.Dispose()
            For i = 0 To _disposeMem.Count - 1
                ReleaseMemObject(_disposeMem(i))
            Next
            _disposeMem.Clear()
            ' TODO: 释放未托管资源(未托管对象)并在以下内容中替代 Finalize()。
            ' TODO: 将大型字段设置为 null。
        End If
        disposedValue = True
    End Sub

    ' TODO: 仅当以上 Dispose(disposing As Boolean)拥有用于释放未托管资源的代码时才替代 Finalize()。
    Protected Overrides Sub Finalize()
        ' 请勿更改此代码。将清理代码放入以上 Dispose(disposing As Boolean)中。
        Dispose(False)
        MyBase.Finalize()
    End Sub

    ' Visual Basic 添加此代码以正确实现可释放模式。
    Public Sub Dispose() Implements IDisposable.Dispose
        ' 请勿更改此代码。将清理代码放入以上 Dispose(disposing As Boolean)中。
        Dispose(True)
        ' TODO: 如果在以上内容中替代了 Finalize()，则取消注释以下行。
        ' GC.SuppressFinalize(Me)
    End Sub
#End Region

End Class